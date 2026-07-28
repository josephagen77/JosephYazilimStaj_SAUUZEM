using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    [Authorize]
    public class AiAssistantController : Controller
    {
        private readonly IAiService _aiService;
        private readonly IVectorDbService _vectorDbService;
        private readonly ICourseDocumentService _courseDocumentService;
        private readonly ICourseService _courseService;

        public AiAssistantController(
            IAiService aiService,
            IVectorDbService vectorDbService,
            ICourseDocumentService courseDocumentService,
            ICourseService courseService)
        {
            _aiService = aiService;
            _vectorDbService = vectorDbService;
            _courseDocumentService = courseDocumentService;
            _courseService = courseService;
        }

        [HttpPost]
        public async Task<IActionResult> AskAi(int courseId, string question, int? documentId, string provider = "gemini", string? userApiKey = null)
        {
            if (string.IsNullOrEmpty(question))
            {
                return Json(new { success = false, response = "Lütfen boş bir soru göndermeyin." });
            }

            try
            {
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
                    relevantTexts = await _vectorDbService.SearchSimilarTextsAsync(
                        collectionName: "lesson_contents",
                        vectorData: questionVector,
                        limit: 3
                    );
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
                            .Where(text => !string.IsNullOrWhiteSpace(text))
                            .Take(5)
                            .ToList() ?? new List<string>();
                    }
                }

                if (relevantTexts.Count == 0)
                {
                    string emptyMessage = selectedDocumentPath == null
                        ? "Bu kurs için cevap üretilecek ders içeriği bulunamadı. Önce haftalık içerik veya doküman ekleyin."
                        : "Seçilen doküman için cevap üretilecek metin bulunamadı. Dokümanı tekrar yükleyip işlendiğinden emin olun.";

                    return Json(new { success = false, response = emptyMessage });
                }

                string context = string.Join("\n\n", relevantTexts);

                string finalPrompt = $@"
                    Sen bu dersin yapay zeka asistanısın. Aşağıda sana bu dersin içeriğinden alınan kaynak metinler (Bağlam) verilmiştir.
                    Lütfen ÖĞRENCİNİN SORUSU'nu sadece ve sadece verilen BAĞLAM'a sadık kalarak, kendi yorumunu veya dışarıdan bilgi eklemeden, akademik ve net bir dilde cevapla.
                    Eğer soru bağlamla ilgili değilse veya bağlamda kesin bir cevabı yoksa, kibarca 'Bu sorunun cevabı ders içeriklerinde yer almamaktadır.' de.

                    SEÇİLEN KAYNAK:
                    {selectedSourceName}

                    BAĞLAM:
                    {context}

                    ÖĞRENCİNİN SORUSU:
                    {question}
                ";

                // Model adı yerine provider ve apiKey gönderiyoruz
                string aiResponse = await _aiService.SummarizeTextAsync(finalPrompt, provider, userApiKey);

                if (IsAiServiceError(aiResponse))
                {
                    if (provider != "gemini") return Json(new { success = false, response = aiResponse }); // ChatGPT/Claude hatasını direkt göster
                    aiResponse = BuildLocalFallbackAnswer(relevantTexts); // Sadece Gemini çökerse yerel yedeğe dön
                }

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
                    Aşağıdaki ders dokümanını öğrencinin hızlıca anlayacağı şekilde Türkçe özetle.
                    En önemli konu başlıklarını, dokümanın kapsamını ve sınav/çalışma açısından dikkat edilmesi gereken noktaları kısa paragraflarla ver.

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
                Aşağıdaki ders dokümanına göre Türkçe {questionCount} adet çoktan seçmeli quiz üret.
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