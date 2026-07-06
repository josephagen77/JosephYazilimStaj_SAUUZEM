using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http; // 🚀 IFormFile kullanımı için eklenen kütüphane
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    [Authorize]
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;
        private readonly IAiService _aiService;
        private readonly IVectorDbService _vectorDbService;
        private readonly ICourseDocumentService _courseDocumentService; 

        
        public CourseController(
            ICourseService courseService,
            IAiService aiService,
            IVectorDbService vectorDbService,
            ICourseDocumentService courseDocumentService) 
        {
            _courseService = courseService;
            _aiService = aiService;
            _vectorDbService = vectorDbService;
            _courseDocumentService = courseDocumentService; 
        }


        // Tüm kursları ana sayfada listeler
        public async Task<IActionResult> Index()
        {
            var courses = await _courseService.GetAllCoursesAsync();
            return View(courses);
        }

        // Kursun detaylarını ve haftalık konularını (Lesson) getirir
        public async Task<IActionResult> Details(int id)
        {
            var course = await _courseService.GetCourseByIdAsync(id);
            if (course == null)
            {
                return NotFound();
            }
            return View(course);
        }


        [HttpPost]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> UploadDocument(int courseId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return RedirectToAction("Details", new { id = courseId });
            }

            try
            {

                await _courseDocumentService.UploadDocumentAsync(courseId, file);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Doküman yüklenirken hata oluştu: {ex.Message}";
            }

            return RedirectToAction("Details", new { id = courseId });
        }


        [HttpPost]
        public async Task<IActionResult> AskAi(int courseId, string question, int? documentId)
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

                // Adım A: Öğrencinin sorusunu Gemini API yardımıyla vektör dizisine çevirmeyi deniyoruz.
                // Embedding başarısız olursa chatbot tamamen durmasın; aşağıda veritabanı içeriğine düşeceğiz.
                List<float>? questionVector = selectedDocumentPath == null
                    ? await _aiService.GetEmbeddingAsync(question)
                    : null;

                if (questionVector != null && questionVector.Count > 0)
                {
                    // Adım B: Qdrant Vector DB üzerinde bu soruya en yakın/en alakalı 3 metin parçasını arıyoruz
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

                // Adım C: Gelen kaynak metinleri tek bir "Context" (Bağlam) bloğu haline getiriyoruz
                string context = relevantTexts.Count > 0
                    ? string.Join("\n\n", relevantTexts)
                    : "Bu kursa ait herhangi bir döküman veya ders içeriği bulunamadı.";

                // Adım D: Gemini'a sınırlarını ve kurallarını çizen akıllı bir RAG Prompt'u hazırlıyoruz
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

                // Adım E: Prompt'u Gemini'a gönderip ders kaynaklarına göre filtrelenmiş cevabı alıyoruz
                string aiResponse = await _aiService.SummarizeTextAsync(finalPrompt);
                if (IsAiServiceError(aiResponse))
                {
                    aiResponse = BuildLocalFallbackAnswer(relevantTexts, aiResponse);
                }

                // JavaScript tarafına (AJAX) başarılı yanıtı döndürüyoruz
                return Json(new { success = true, response = aiResponse });
            }
            catch (Exception ex)
            {
                // Herhangi bir sunucu veya Docker bağlantı koptuğunda güvenli hata mesajı fırlatıyoruz
                return Json(new { success = false, response = $"Teknik bir hata oluştu: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DocumentSummary(int courseId, int documentId)
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

                string summary = await _aiService.SummarizeTextAsync(summaryPrompt);
                if (IsAiServiceError(summary))
                {
                    summary = BuildLocalDocumentSummary(document.FileName, documentTexts, summary);
                }

                return Json(new { success = true, response = summary });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, response = $"Doküman özeti alınırken teknik bir hata oluştu: {ex.Message}" });
            }
        }

        private static bool IsAiServiceError(string response)
        {
            return response.Contains("Gemini API", StringComparison.OrdinalIgnoreCase) ||
                   response.Contains("Yapay zeka servisi", StringComparison.OrdinalIgnoreCase) ||
                   response.Contains("Özet oluşturulurken teknik bir hata", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLocalFallbackAnswer(List<string> relevantTexts, string aiError)
        {
            string sourcePreview = string.Join(
                "\n\n",
                relevantTexts
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Take(2)
                    .Select(text => text.Length > 700 ? text.Substring(0, 700) + "..." : text));

            return $@"Gemini yanıt üretirken hata verdi, ancak ders kaynaklarından ilgili içerik bulundu.

Teknik detay: {aiError}

Ders kaynaklarından bulunan içerik:
{sourcePreview}";
        }

        private static string BuildLocalDocumentSummary(string fileName, List<string> documentTexts, string aiError)
        {
            string preview = string.Join(
                "\n\n",
                documentTexts
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Take(2)
                    .Select(text => text.Length > 900 ? text.Substring(0, 900) + "..." : text));

            return $@"{fileName} dokümanından metin çıkarıldı, ancak Gemini otomatik özet üretemedi.

Teknik detay: {aiError}

Dokümandan kısa önizleme:
{preview}";
        }
    }
}
