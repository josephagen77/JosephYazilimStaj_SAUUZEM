using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private readonly ICourseDocumentService _courseDocumentService;

        // 🎯 YENİ: Vektör arama ve embedding için servisler eklendi
        private readonly IAiService _aiService;
        private readonly IVectorDbService _vectorDbService;

        public CourseController(
            ICourseService courseService,
            ICourseDocumentService courseDocumentService,
            IAiService aiService, // 🎯 YENİ
            IVectorDbService vectorDbService) // 🎯 YENİ
        {
            _courseService = courseService;
            _courseDocumentService = courseDocumentService;
            _aiService = aiService;
            _vectorDbService = vectorDbService;
        }

        // 🎯 GÜNCELLENDİ: Arama çubuğu desteği eklendi (Semantic Search)
        public async Task<IActionResult> Index(string? searchQuery)
        {
            var courses = await _courseService.GetAllCoursesAsync();

            // Eğer öğrenci bir arama yaptıysa Qdrant'a sor
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                // Arama metnini vektöre çevir (Daima varsayılan gemini modeli ile)
                var searchVector = await _aiService.GetEmbeddingAsync(searchQuery);

                if (searchVector != null && searchVector.Count > 0)
                {
                    // Qdrant'tan eşleşen CourseId'leri getir
                    var matchingCourseIds = await _vectorDbService.SearchSimilarCoursesAsync(searchVector, limit: 10);

                    if (matchingCourseIds.Any())
                    {
                        // Sadece vektör veritabanından dönen ID'lerle eşleşen kursları filtrele
                        courses = courses.Where(c => matchingCourseIds.Contains(c.Id));
                        ViewBag.SearchQuery = searchQuery;
                    }
                    else
                    {
                        // Eşleşme yoksa listeyi boşalt
                        courses = Enumerable.Empty<Course>();
                        ViewBag.SearchQuery = searchQuery;
                        ViewBag.NoResults = true;
                    }
                }
            }

            return View(courses);
        }

        public async Task<IActionResult> Details(int id)
        {
            var course = await _courseService.GetCourseByIdAsync(id);
            if (course == null)
            {
                return NotFound();
            }

            await _courseDocumentService.EnsureDocumentTopicLessonsAsync(id);
            course = await _courseService.GetCourseByIdAsync(id);
            if (course == null)
            {
                return NotFound();
            }

            return View(course);
        }

        // --- COURSE MANAGEMENT (CREATE, EDIT, DELETE) ---

        [HttpGet]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        public IActionResult Create()
        {
            return View(new Course());
        }

        [HttpPost]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Course course)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _courseService.AddCourseAsync(course);

                    // 🎯 YENİ: Yeni kurs oluşturulunca, arama için Qdrant'a indeksle
                    await IndexCourseForSearchAsync(course);

                    TempData["SuccessMessage"] = "Ders başarıyla oluşturuldu ve yapay zeka aramasına eklendi.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Ders oluşturulurken hata oluştu: {ex.Message}";
                }
            }
            return View(course);
        }

        [HttpGet]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        public async Task<IActionResult> Edit(int id)
        {
            var course = await _courseService.GetCourseByIdAsync(id);
            if (course == null)
            {
                return NotFound();
            }
            return View(course);
        }

        [HttpPost]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Course course)
        {
            if (id != course.Id)
            {
                return BadRequest();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _courseService.UpdateCourseAsync(course);

                    // 🎯 YENİ: Kurs güncellenince Qdrant indeksini de güncelle
                    await IndexCourseForSearchAsync(course);

                    TempData["SuccessMessage"] = "Ders başarıyla güncellendi.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Ders güncellenirken hata oluştu: {ex.Message}";
                }
            }
            return View(course);
        }

        [HttpGet]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        public async Task<IActionResult> Delete(int id)
        {
            var course = await _courseService.GetCourseByIdAsync(id);
            if (course == null)
            {
                return NotFound();
            }
            return View(course);
        }

        [HttpPost, ActionName("Delete")]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _courseService.DeleteCourseAsync(id);
                TempData["SuccessMessage"] = "Ders başarıyla silindi.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Ders silinirken hata oluştu: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // --- DOCUMENT MANAGEMENT ---

        [HttpPost]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        public async Task<IActionResult> UploadDocument(int courseId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return RedirectToAction("Details", new { id = courseId });
            }

            try
            {
                await _courseDocumentService.UploadDocumentAsync(courseId, file);
                TempData["SuccessMessage"] = "Doküman başarıyla yüklendi ve yapay zeka için indekslendi.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Doküman yüklenirken hata oluştu: {ex.Message}";
            }

            return RedirectToAction("Details", new { id = courseId });
        }

        [HttpPost]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        public async Task<IActionResult> DeleteDocument(int id, int courseId)
        {
            try
            {
                await _courseDocumentService.DeleteDocumentAsync(id);
                TempData["SuccessMessage"] = "Döküman, SQL kayıtları ve yapay zeka bellek vektörleri başarıyla silindi.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Döküman silinirken teknik bir hata oluştu: {ex.Message}";
            }

            return RedirectToAction("Details", new { id = courseId });
        }

        // 🎯 YENİ: Yardımcı Metod - Kursu AI veritabanına kaydeder
        private async Task IndexCourseForSearchAsync(Course course)
        {
            // Öğrenci arama yaptığında eşleşmesi için kursun bilgilerini birleştiriyoruz
            string searchData = $"Kurs Adı: {course.Title}. Açıklama: {course.Description}. Kod: {course.CourseCode}";

            // Metni vektöre çevir
            var courseVector = await _aiService.GetEmbeddingAsync(searchData);

            if (courseVector != null && courseVector.Count > 0)
            {
                // Qdrant'a kaydet (Eski kayıt varsa ID üzerinden otomatik günceller)
                await _vectorDbService.SaveCourseVectorAsync(course.Id, courseVector, searchData);
            }
        }
    }
}