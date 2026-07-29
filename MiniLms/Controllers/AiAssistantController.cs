using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Claims; // 🎯 YENİ: Kullanıcı ID'sini almak için eklendi
using MiniLms.Data; // 🎯 YENİ: Veritabanına doğrudan erişim için eklendi
using MiniLms.Models;
using Microsoft.EntityFrameworkCore;

namespace MiniLms.Controllers
{
    [Authorize]
    public class AiAssistantController : Controller
    {
        private readonly IAiService _aiService;
        private readonly IVectorDbService _vectorDbService;
        private readonly ICourseDocumentService _courseDocumentService;
        private readonly ICourseService _courseService;
        private readonly ApplicationDbContext _context; // 🎯 YENİ: Chat geçmişini kaydetmek için

        public AiAssistantController(
            IAiService aiService,
            IVectorDbService vectorDbService,
            ICourseDocumentService courseDocumentService,
            ICourseService courseService,
            ApplicationDbContext context) // 🎯 YENİ: Constructor'a eklendi
        {
            _aiService = aiService;
            _vectorDbService = vectorDbService;
            _courseDocumentService = courseDocumentService;
            _courseService = courseService;
            _context = context;
        }

        // 🎯 YENİ: Sayfa yüklendiğinde eski sohbetleri getiren endpoint
        [HttpGet]
        public async Task<IActionResult> GetChatHistory(int courseId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Json(new { success = false });

            var history = await _context.ChatMessages
                .Where(m => m.CourseId == courseId && m.UserId == userId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new { role = m.Role, content = m.Content, time = m.Timestamp.ToString("HH:mm") })
                .ToListAsync();

            return Json(new { success = true, history });
        }

        [HttpPost]
        public async Task<IActionResult> AskAi(int courseId, string question, int? documentId, string provider = "gemini", string? userApiKey = null)
        {
            if (string.IsNullOrEmpty(question))
            {
                return Json(new { success = false, response = "Lütfen boş bir soru göndermeyin." });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, response = "Oturum süreniz dolmuş, lütfen tekrar giriş yapın." });
            }

            try
            {
                // 1. İlgili metinleri vektör aramasından getir
                var relevantTexts = new List<string>();
                string selectedSourceName = "Tüm ders kaynakları";
                string? selectedDocumentPath = null;

                if (documentId.HasValue && documentId.Value > 0)
                {
                    var selectedDocument = await _courseDocumentService.GetDocumentByIdAsync(documentId.Value);
                    if (selectedDocument == null || selectedDocument.CourseId != courseId)
                    {
                        return Json(new { success = false, response = "Seçilen doküman bu derse ait değil veya bulunamadı." });
                    }

                    selectedSourceName = selectedDocument.FileName;
                    selectedDocumentPath = selectedDocument.FilePath;
                }

                List<float>? questionVector = selectedDocumentPath == null
                    ? await _aiService.GetEmbeddingAsync(question)
                    : null;

                if (questionVector != null && questionVector.Count > 0)
                {
                    relevantTexts = await _vectorDbService.SearchSimilarTextsAsync("lesson_contents", questionVector, 3);
                }

                if (relevantTexts == null || relevantTexts.Count == 0)
                {
                    if (documentId.HasValue && documentId.Value > 0)
                    {
                        relevantTexts = await _courseDocumentService.GetDocumentTextChunksAsync(documentId.Value);
                    }
                    else
                    {
                        var course = await _courseService.GetCourseByIdAsync(courseId);
                        relevantTexts = course?.Lessons?
                            .SelectMany(lesson => lesson.Contents ?? Enumerable.Empty<Models.LessonContent>())
                            .Select(content => !string.IsNullOrWhiteSpace(content.Body) ? content.Body : content.Text)
                            .Where(text => !string.IsNullOrWhiteSpace(text)).Take(5).ToList() ?? new List<string>();
                    }
                }

                if (relevantTexts.Count == 0)
                {
                    string emptyMessage = selectedDocumentPath == null
                        ? "Bu kurs için cevap üretilecek ders içeriği bulunamadı. Önce haftalık içerik veya doküman ekleyin."
                        : "Seçilen doküman için cevap üretilecek metin bulunamadı. Dokümanı tekrar yükleyip işlendiğinden emin olun.";
                    return Json(new { success = false, response = emptyMessage });
                }

                // 2. 🎯 YENİ: Geçmiş mesajları veritabanından getir (Son 6 mesaj / 3 soru-cevap çifti)
                var previousMessages = await _context.ChatMessages
                    .Where(m => m.CourseId == courseId && m.UserId == userId)
                    .OrderByDescending(m => m.Timestamp)
                    .Take(6)
                    .ToListAsync();

                previousMessages.Reverse(); // Kronolojik sıraya koy

                string chatHistoryContext = string.Join("\n", previousMessages.Select(m =>
                    $"{(m.Role == "user" ? "ÖĞRENCİ" : "ASİSTAN")}: {m.Content}"));

                string context = string.Join("\n\n", relevantTexts);

                // 3. 🎯 GÜNCELLENDİ: Final Prompt'a esneklik tanındı (Çoklu Dil Desteği)
                string finalPrompt = $@"
    Sen bu dersin eğitim asistanısın. Aşağıda sana bu dersin içeriğinden alınan kaynak metinler (Bağlam) ve Öğrenci ile olan geçmiş sohbetiniz verilmiştir.

    KURALLAR:
    1. Eğer öğrencinin sorusu teknik bir ders konusu ise SADECE BAĞLAM'a sadık kalarak, akademik ve net bir dilde cevapla. Dışarıdan yeni bilgi ekleme.
    2. Eğer öğrenci geçmiş bir cevabı 'daha basit anlat', 'kısalt', 'örneklendir' veya 'kolayca anlat' gibi şekillerde değiştirmeyi istiyorsa, SOHBET GEÇMİŞİNİ referans alarak istenen formatlamayı yap.
    3. Eğitici ve destekleyici bir üslup kullan.
    4. SADECE soru bağlamla ve geçmiş sohbetle tamamen alakasızsa (örneğin hava durumu, maç sonuçları vb.), kibarca sorunun ders içeriğinde olmadığını belirt.
    5. DİL KURALI (ÖNEMLİ): Öğrenci HANGİ DİLDE soru soruyorsa (İngilizce, Türkçe, Arapça vb.) veya HANGİ DİLDE yanıt istiyorsa, SADECE O DİLDE yanıt ver.

    SEÇİLEN KAYNAK:
    {selectedSourceName}

    BAĞLAM:
    {context}

    GEÇMİŞ SOHBET (Bağlamı Hatırlaman İçin):
    {(string.IsNullOrWhiteSpace(chatHistoryContext) ? "Henüz geçmiş sohbet yok." : chatHistoryContext)}

    ÖĞRENCİNİN YENİ SORUSU:
    {question}
";

                // 4. Yapay Zeka'ya sor
                string aiResponse = await _aiService.SummarizeTextAsync(finalPrompt, provider, userApiKey);

                if (IsAiServiceError(aiResponse))
                {
                    if (provider != "gemini") return Json(new { success = false, response = aiResponse });
                    aiResponse = BuildLocalFallbackAnswer(relevantTexts);
                }

                // 5. 🎯 YENİ: Kullanıcının sorusunu ve AI'ın cevabını veritabanına kaydet
                var userMessage = new ChatMessage { UserId = userId, CourseId = courseId, Role = "user", Content = question, Timestamp = DateTime.Now };
                var modelMessage = new ChatMessage { UserId = userId, CourseId = courseId, Role = "model", Content = aiResponse, Timestamp = DateTime.Now.AddSeconds(1) };

                await _context.ChatMessages.AddRangeAsync(userMessage, modelMessage);
                await _context.SaveChangesAsync();

                return Json(new { success = true, response = aiResponse });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, response = $"Teknik bir hata oluştu: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DocumentSummary(int courseId, int documentId, string provider = "gemini", string? userApiKey = null)
        {
            try
            {
                var document = await _courseDocumentService.GetDocumentByIdAsync(documentId);
                if (document == null || document.CourseId != courseId)
                {
                    return Json(new { success = false, response = "Seçilen doküman bu derse ait değil veya bulunamadı." });
                }

                var documentTexts = await _courseDocumentService.GetDocumentTextChunksAsync(documentId, maxChunks: 4);
                if (documentTexts.Count == 0)
                {
                    return Json(new { success = false, response = "Bu dokümandan özet üretilecek metin çıkarılamadı." });
                }

                string sourceText = string.Join("\n\n", documentTexts);
                string summaryPrompt = $@"
    Aşağıdaki ders dokümanını öğrencinin hızlıca anlayacağı şekilde özetle.
    En önemli konu başlıklarını, dokümanın kapsamını ve sınav/çalışma açısından dikkat edilmesi gereken noktaları kısa paragraflarla ver.
    
    DİL KURALI: Özetin dilini, kaynak dokümanın orijinal dilinde oluştur. (Örneğin, doküman İngilizce ise İngilizce özetle).

    DOKÜMAN ADI:
    {document.FileName}

    DOKÜMAN METNİ:
    {sourceText}
";

                string summary = await _aiService.SummarizeTextAsync(summaryPrompt, provider, userApiKey);

                if (IsAiServiceError(summary))
                {
                    if (provider != "gemini") return Json(new { success = false, response = summary });
                    summary = BuildLocalDocumentSummary(document.FileName, documentTexts);
                }

                return Json(new { success = true, response = summary });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, response = $"Doküman özeti alınırken teknik bir hata oluştu: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DocumentQuizSession(int courseId, int documentId, int questionCount = 5, string difficulty = "mixed", string provider = "gemini", string? userApiKey = null)
        {
            try
            {
                var document = await _courseDocumentService.GetDocumentByIdAsync(documentId);
                if (document == null || document.CourseId != courseId)
                {
                    return Json(new { success = false, response = "Seçilen doküman bu derse ait değil veya bulunamadı." });
                }

                questionCount = Math.Clamp(questionCount, 3, 10);

                var documentTexts = await _courseDocumentService.GetDocumentTextChunksAsync(documentId, maxChunks: 8);
                if (documentTexts.Count == 0)
                {
                    return Json(new { success = false, response = "Bu dokümandan quiz üretilecek metin çıkarılamadı." });
                }

                difficulty = NormalizeDifficulty(difficulty);
                var questions = await BuildInteractiveDocumentQuizAsync(document.FileName, documentTexts, questionCount, difficulty, provider, userApiKey);

                if (questions.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        response = "Bu dokümandan kaliteli quiz sorusu çıkarılamadı. API Anahtarınızın doğru olduğundan veya dokümanda yeterli metin olduğundan emin olun."
                    });
                }

                return Json(new
                {
                    success = true,
                    title = $"{document.FileName} Quiz",
                    questions
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, response = $"Quiz hazırlanırken teknik bir hata oluştu: {ex.Message}" });
            }
        }

        // --- Helper & Parsing Methods ---
        private static bool IsAiServiceError(string response)
        {
            return response.Contains("API Anahtarı geçerli değil", StringComparison.OrdinalIgnoreCase) ||
                   response.Contains("Gemini API", StringComparison.OrdinalIgnoreCase) ||
                   response.Contains("Yapay zeka servisi", StringComparison.OrdinalIgnoreCase) ||
                   response.Contains("Özet oluşturulurken teknik bir hata", StringComparison.OrdinalIgnoreCase) ||
                   response.Contains("boş yanıt döndürdü", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLocalFallbackAnswer(List<string> relevantTexts)
        {
            string sourcePreview = string.Join("\n\n", relevantTexts.Where(t => !string.IsNullOrWhiteSpace(t)).Take(2));
            return $"Kurumsal yapay zeka bağlantısı şu anda kullanılamıyor, ancak ders kaynaklarından ilgili içerik bulundu:\n\n{sourcePreview}";
        }

        private static string BuildLocalDocumentSummary(string fileName, List<string> documentTexts)
        {
            string preview = string.Join("\n\n", documentTexts.Where(t => !string.IsNullOrWhiteSpace(t)).Take(2));
            return $"{fileName} dokümanından metin çıkarıldı, ancak otomatik özet şu anda kullanılamıyor:\n\n{preview}";
        }

        private async Task<List<QuizQuestionDto>> BuildInteractiveDocumentQuizAsync(string fileName, List<string> documentTexts, int questionCount, string difficulty, string provider, string? userApiKey)
        {
            string sourceText = string.Join("\n\n", documentTexts);
            string difficultyInstruction = difficulty switch
            {
                "easy" => "Sorular kolay seviyede olsun.",
                "medium" => "Sorular orta seviyede olsun.",
                "hard" => "Sorular zor seviyede olsun.",
                _ => "Sorular karma seviyede olsun."
            };

            string jsonPrompt = $@"
    Aşağıdaki ders dokümanına göre {questionCount} adet çoktan seçmeli quiz üret.
    
    DİL KURALI: Quiz sorularını, seçeneklerini ve açıklamalarını kaynak dokümanın yazıldığı dilde oluştur.

    Sadece geçerli JSON döndür. Markdown veya kod bloğu kullanma.
    JSON formatı:
    [
      {{
        ""question"": ""Soru metni"",
        ""options"": [""A seçeneği"", ""B seçeneği"", ""C seçeneği"", ""D seçeneği""],
        ""correctIndex"": 0,
        ""explanation"": ""Gerekçe"",
        ""topic"": ""Konu"",
        ""difficulty"": ""Orta"",
        ""bloomLevel"": ""Anlama"",
        ""sourceHint"": ""İpucu"",
        ""whyWrong"": [""A neden yanlış"", ""B neden yanlış"", ""C neden yanlış"", ""D neden yanlış""]
      }}
    ]
    {difficultyInstruction}
    DOKÜMAN ADI: {fileName}
    DOKÜMAN METNİ: {sourceText}
";

            string aiResponse = await _aiService.SummarizeTextAsync(jsonPrompt, provider, userApiKey);

            if (!IsAiServiceError(aiResponse))
            {
                var parsed = TryParseQuizJson(aiResponse, questionCount);
                if (parsed.Count > 0) return parsed;
            }

            return new List<QuizQuestionDto>();
        }

        private static List<QuizQuestionDto> TryParseQuizJson(string json, int questionCount)
        {
            try
            {
                json = json.Trim();
                int start = json.IndexOf('[');
                int end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    json = json.Substring(start, end - start + 1);
                }

                var questions = JsonSerializer.Deserialize<List<QuizQuestionDto>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<QuizQuestionDto>();

                return questions.Take(questionCount).ToList();
            }
            catch
            {
                return new List<QuizQuestionDto>();
            }
        }

        private static string NormalizeDifficulty(string difficulty)
        {
            difficulty = (difficulty ?? "mixed").Trim().ToLowerInvariant();
            return difficulty is "easy" or "medium" or "hard" or "mixed" ? difficulty : "mixed";
        }

        private class QuizQuestionDto
        {
            public string Question { get; set; } = string.Empty;
            public List<string> Options { get; set; } = new();
            public int CorrectIndex { get; set; }
            public string Explanation { get; set; } = string.Empty;
            public string Topic { get; set; } = string.Empty;
            public string Difficulty { get; set; } = string.Empty;
            public string BloomLevel { get; set; } = string.Empty;
            public string SourceHint { get; set; } = string.Empty;
            public List<string> WhyWrong { get; set; } = new();
        }
    }
}