using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    [Authorize]
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;
        private readonly ICourseDocumentService _courseDocumentService;

        public CourseController(
            ICourseService courseService,
            ICourseDocumentService courseDocumentService)
        {
            _courseService = courseService;
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

            await _courseDocumentService.EnsureDocumentTopicLessonsAsync(id);
            course = await _courseService.GetCourseByIdAsync(id);
            if (course == null)
            {
                return NotFound();
            }

            return View(course);
        }

        // --- COURSE MANAGEMENT (CREATE, EDIT, DELETE) ---

        // GET: Course/Create
        [HttpGet]
        [Authorize(Policy = UserPolicies.TeacherOnly)]
        public IActionResult Create()
        {
            return View(new Course());
        }

        // POST: Course/Create
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
                    TempData["SuccessMessage"] = "Ders başarıyla oluşturuldu.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Ders oluşturulurken hata oluştu: {ex.Message}";
                }
            }
            return View(course);
        }

        // GET: Course/Edit/5
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

        // POST: Course/Edit/5
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

        // GET: Course/Delete/5
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

        // POST: Course/Delete/5
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
    }
}