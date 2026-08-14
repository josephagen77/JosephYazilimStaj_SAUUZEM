using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Claims;
using MiniLms.Data;
using MiniLms.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace MiniLms.Controllers
{
    [Authorize]
    public class AiAssistantController : Controller
    {
        private readonly IAiService _aiService;
        private readonly IVectorDbService _vectorDbService;
        private readonly ICourseDocumentService _courseDocumentService;
        private readonly ICourseService _courseService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;

        public AiAssistantController(
            IAiService aiService,
            IVectorDbService vectorDbService,
            ICourseDocumentService courseDocumentService,
            ICourseService courseService,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            _aiService = aiService;
            _vectorDbService = vectorDbService;
            _courseDocumentService = courseDocumentService;
            _courseService = courseService;
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
        }

        private async Task<(bool success, string apiKey, string errorMessage)> ResolveApiKeyAsync(string userId, string providerKey)
        {
            var provider = await _context.AiProviders.FirstOrDefaultAsync(p => p.ProviderKey == providerKey.ToLower().Trim());

            if (provider != null && !provider.IsActive)
            {
                return (false, "", $"{provider.Name} Sistem Yöneticisi tarafından geçici olarak devre dışı bırakılmıştır.");
            }

            // 1. GEMINI (Varsayılan Kurumsal Model - Sadece Sistem Yöneticisi Tarafından Yönetilir):
            if (providerKey.ToLower().Trim() == "gemini")
            {
                string geminiKey = !string.IsNullOrEmpty(provider?.GlobalApiKey)
                    ? provider.GlobalApiKey
                    : (_configuration["AiServices:Gemini:ApiKey"] ?? _configuration["GeminiApiKey"] ?? _configuration["Gemini:ApiKey"] ?? string.Empty);

                if (string.IsNullOrEmpty(geminiKey))
                {
                    return (false, "", "Sistemde tanımlı varsayılan Google Gemini API anahtarı bulunamadı. Lütfen Sistem Yöneticisi ile iletişime geçin.");
                }

                return (true, geminiKey, "");
            }

            // 2. DİĞER MODELLER (ChatGPT, Claude vb. - Öğrencinin Kendi Anahtarı Gerekir):
            if (provider == null)
                return (false, "", "Geçersiz yapay zeka sağlayıcısı.");

            // Sistem yöneticisi bu modele global bir kurumsal anahtar tanımlamışsa onu kullanabilir
            if (!string.IsNullOrEmpty(provider.GlobalApiKey))
                return (true, provider.GlobalApiKey, "");

            // Aksi takdirde öğrencinin kendi profiline kaydettiği şahsi anahtar kullanılır
            var userSavedKey = await _context.UserAiProviders
                .FirstOrDefaultAsync(u => u.UserId == userId && u.AiProviderId == provider.Id);

            if (userSavedKey == null || string.IsNullOrEmpty(userSavedKey.ApiKey))
            {
                return (false, "", $"{provider.Name} modelini kullanabilmek için kendi API anahtarınızı tanımlamanız gerekmektedir. Lütfen sağ üst menüden 'API Anahtarlarım' sayfasına giderek anahtarınızı ekleyin veya varsayılan Gemini modelini kullanın.");
            }

            return (true, userSavedKey.ApiKey, "");
        }

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
        public async Task<IActionResult> AskAi(int courseId, string question, int? documentId, string provider = "gemini")
        {
            if (string.IsNullOrEmpty(question)) return Json(new { success = false, response = "Lütfen boş bir soru göndermeyin." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Json(new { success = false, response = "Oturum süreniz dolmuş, lütfen tekrar giriş yapın." });

            try
            {
                var apiKeyResult = await ResolveApiKeyAsync(userId!, provider);
                if (!apiKeyResult.success)
                {
                    return Json(new { success = false, response = $"⚠️ Hata: {apiKeyResult.errorMessage}" });
                }

                string theApiKey = apiKeyResult.apiKey;

                var relevantTexts = new List<string>();
                string selectedSourceName = "Tüm ders kaynakları";
                string? selectedDocumentPath = null;

                if (documentId.HasValue && documentId.Value > 0)
                {
                    var selectedDocument = await _courseDocumentService.GetDocumentByIdAsync(documentId.Value);
                    if (selectedDocument == null || selectedDocument.CourseId != courseId)
                        return Json(new { success = false, response = "Seçilen doküman bu derse ait değil veya bulunamadı." });

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
                        relevantTexts = await _courseDocumentService.GetDocumentTextChunksAsync(documentId.Value);
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

                var previousMessages = await _context.ChatMessages
                    .Where(m => m.CourseId == courseId && m.UserId == userId)
                    .OrderByDescending(m => m.Timestamp)
                    .Take(6)
                    .ToListAsync();

                previousMessages.Reverse();

                string chatHistoryContext = string.Join("\n", previousMessages.Select(m =>
                    $"{(m.Role == "user" ? "ÖĞRENCİ" : "ASİSTAN")}: {m.Content}"));

                string context = string.Join("\n\n", relevantTexts);

                string finalPrompt = $@"
    Sen bu dersin eğitim asistanısın. Aşağıda sana bu dersin içeriğinden alınan kaynak metinler (Bağlam) ve Öğrenci ile olan geçmiş sohbetiniz verilmiştir.

    KURALLAR:
    1. Teknik ders konularında SADECE BAĞLAM'a sadık kalarak akademik bir dilde cevapla.
    2. Öğrenci geçmiş bir cevabı 'daha basit anlat', 'kısalt' vb. diyorsa SOHBET GEÇMİŞİNİ referans al.
    3. DİL KURALI: Öğrenci HANGİ DİLDE soru soruyorsa SADECE O DİLDE yanıt ver.

    SEÇİLEN KAYNAK: {selectedSourceName}
    BAĞLAM: {context}
    GEÇMİŞ SOHBET: {(string.IsNullOrWhiteSpace(chatHistoryContext) ? "Henüz geçmiş sohbet yok." : chatHistoryContext)}
    ÖĞRENCİNİN YENİ SORUSU: {question}
";
                string aiResponse = await _aiService.SummarizeTextAsync(finalPrompt, provider, theApiKey);

                if (IsAiServiceError(aiResponse))
                {
                    if (provider != "gemini") return Json(new { success = false, response = aiResponse });
                    aiResponse = BuildLocalFallbackAnswer(relevantTexts);
                }

                var userMessage = new ChatMessage { UserId = userId!, CourseId = courseId, Role = "user", Content = question, Timestamp = DateTime.Now };
                var modelMessage = new ChatMessage { UserId = userId!, CourseId = courseId, Role = "model", Content = aiResponse, Timestamp = DateTime.Now.AddSeconds(1) };

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
        public async Task<IActionResult> DocumentSummary(int courseId, int documentId, string provider = "gemini")
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Json(new { success = false, response = "Oturum süresi dolmuş." });

                var apiKeyResult = await ResolveApiKeyAsync(userId!, provider);
                if (!apiKeyResult.success) return Json(new { success = false, response = $"⚠️ Hata: {apiKeyResult.errorMessage}" });

                var document = await _courseDocumentService.GetDocumentByIdAsync(documentId);
                if (document == null || document.CourseId != courseId) return Json(new { success = false, response = "Seçilen doküman bu derse ait değil." });

                // 🎯 IMPROVED: Increased maxChunks to 10 so it reads much more of the document
                var documentTexts = await _courseDocumentService.GetDocumentTextChunksAsync(documentId, maxChunks: 10);
                if (documentTexts.Count == 0) return Json(new { success = false, response = "Bu dokümandan özet üretilecek metin çıkarılamadı." });

                string sourceText = string.Join("\n\n", documentTexts);

                // 🎯 IMPROVED: Detailed structuring constraints in the prompt
                string summaryPrompt = $@"
    Sen bu dersin yapay zeka asistanısın. Aşağıdaki ders dokümanını DETAYLI VE KAPSAMLI bir şekilde özetle.

    YAPILANDIRMA KURALLARI (Lütfen Markdown formatında yanıt ver):
    1. **Genel Bakış:** Dokümanın ana amacını 2-3 cümleyle açıklayan bir giriş yap.
    2. **Kilit Kavramlar:** Dokümanda geçen önemli tanım ve kavramları maddeler halinde listele.
    3. **Detaylı Bölüm Özeti:** Konuyu alt başlıklar halinde inceleyip ana fikirleri detaylandır.
    4. **Önemli Çıkarımlar:** Öğrencinin sınavlarda/derslerde bilmesi gereken kritik noktaları vurgula.

    DİL KURALI: Özetin dilini, kaynak dokümanın orijinal dilinde oluştur.

    DOKÜMAN ADI: {document.FileName}
    DOKÜMAN METNİ: {sourceText}
";
                string summary = await _aiService.SummarizeTextAsync(summaryPrompt, provider, apiKeyResult.apiKey);

                if (IsAiServiceError(summary))
                {
                    if (provider != "gemini") return Json(new { success = false, response = summary });
                    summary = BuildLocalDocumentSummary(document.FileName, documentTexts);
                }

                return Json(new { success = true, response = summary });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, response = $"Teknik bir hata oluştu: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DocumentQuizSession(int courseId, int documentId, int questionCount = 5, string difficulty = "mixed", string provider = "gemini")
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Json(new { success = false, response = "Oturum süresi dolmuş." });

                var apiKeyResult = await ResolveApiKeyAsync(userId!, provider);
                if (!apiKeyResult.success) return Json(new { success = false, response = $"⚠️ Hata: {apiKeyResult.errorMessage}" });

                var document = await _courseDocumentService.GetDocumentByIdAsync(documentId);
                if (document == null || document.CourseId != courseId) return Json(new { success = false, response = "Seçilen doküman bu derse ait değil." });

                questionCount = Math.Clamp(questionCount, 3, 10);
                var documentTexts = await _courseDocumentService.GetDocumentTextChunksAsync(documentId, maxChunks: 8);

                if (documentTexts.Count == 0) return Json(new { success = false, response = "Bu dokümandan quiz üretilecek metin çıkarılamadı." });

                difficulty = NormalizeDifficulty(difficulty);
                var questions = await BuildInteractiveDocumentQuizAsync(document.FileName, documentTexts, questionCount, difficulty, provider, apiKeyResult.apiKey);

                if (questions.Count == 0)
                {
                    return Json(new { success = false, response = "Bu dokümandan kaliteli quiz sorusu çıkarılamadı. API Anahtarınızın doğru olduğundan veya dokümanda yeterli metin olduğundan emin olun." });
                }

                return Json(new { success = true, title = $"{document.FileName} Quiz", questions });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, response = $"Quiz hazırlanırken teknik bir hata oluştu: {ex.Message}" });
            }
        }

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

        private async Task<List<QuizQuestionDto>> BuildInteractiveDocumentQuizAsync(string fileName, List<string> documentTexts, int questionCount, string difficulty, string provider, string apiKey)
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
        ""whyWrong"": [""A neden yanlış"", ""B neden yanlış""]
      }}
    ]
    {difficultyInstruction}
    DOKÜMAN ADI: {fileName}
    DOKÜMAN METNİ: {sourceText}
";
            string aiResponse = await _aiService.SummarizeTextAsync(jsonPrompt, provider, apiKey);

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