using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.Models.Enums;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MiniLms.Controllers
{
    [Authorize(Policy = UserPolicies.TeacherOnly)]
    public class EnrollmentController : Controller
    {
        private readonly IEnrollmentService _enrollmentService;
        private readonly IStudentService _studentService;
        private readonly ICourseService _courseService;

        public EnrollmentController(
            IEnrollmentService enrollmentService,
            IStudentService studentService,
            ICourseService courseService)
        {
            _enrollmentService = enrollmentService;
            _studentService = studentService;
            _courseService = courseService;
        }

        // GET: Enrollment
        public async Task<IActionResult> Index()
        {
            var enrollments = await _enrollmentService.GetAllEnrollmentsAsync();

            // 🎯 YENİ: Sınıf listesini ve öğrenci sayılarını göstermek için tüm kursları çekip ViewBag ile gönderiyoruz.
            var courses = await _courseService.GetAllCoursesAsync();
            ViewBag.Courses = courses;

            return View(enrollments);
        }

        // GET: Enrollment/Create
        public async Task<IActionResult> Create()
        {
            var students = await _studentService.GetAllStudentsAsync();
            var courses = await _courseService.GetAllCoursesAsync();

            ViewBag.StudentId = new SelectList(students.Select(s => new { Id = s.Id, DisplayText = $"{s.StudentNumber} - {s.FirstName} {s.LastName}" }), "Id", "DisplayText");
            ViewBag.CourseId = new SelectList(courses.Select(c => new { Id = c.Id, DisplayText = $"{c.CourseCode} - {c.Title}" }), "Id", "DisplayText");

            return View();
        }

        // POST: Enrollment/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Enrollment enrollment)
        {
            ModelState.Remove("Student");
            ModelState.Remove("Course");

            enrollment.EnrollmentDate = DateTime.Now;

            if (ModelState.IsValid)
            {
                try
                {
                    await _enrollmentService.EnrollStudentAsync(enrollment);
                    TempData["SuccessMessage"] = "Ders kaydı başarıyla oluşturuldu.";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(string.Empty, "Ders kaydı sırasında beklenmeyen bir hata oluştu.");
                }
            }

            var students = await _studentService.GetAllStudentsAsync();
            var courses = await _courseService.GetAllCoursesAsync();

            ViewBag.StudentId = new SelectList(students.Select(s => new { Id = s.Id, DisplayText = $"{s.StudentNumber} - {s.FirstName} {s.LastName}" }), "Id", "DisplayText", enrollment.StudentId);
            ViewBag.CourseId = new SelectList(courses.Select(c => new { Id = c.Id, DisplayText = $"{c.CourseCode} - {c.Title}" }), "Id", "DisplayText", enrollment.CourseId);

            return View(enrollment);
        }

        // GET: Enrollment/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var enrollment = await _enrollmentService.GetEnrollmentByIdAsync(id);
            if (enrollment == null)
            {
                return NotFound();
            }
            return View(enrollment);
        }

        // POST: Enrollment/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _enrollmentService.RemoveEnrollmentAsync(id);
                TempData["SuccessMessage"] = "Ders kaydı silindi.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "Ders kaydı silinirken bir hata meydana geldi.";
                var enrollment = await _enrollmentService.GetEnrollmentByIdAsync(id);
                return View(enrollment);
            }
        }

        // ---------------------------------------------------------
        // 🎯 YENİ: KURS BAZLI SINIF YÖNETİMİ (MANAGE EKRANI İÇİN)
        // ---------------------------------------------------------

        // GET: Enrollment/Manage/5 (Bir kursun içine girip öğrencileri yönetme)
        public async Task<IActionResult> Manage(int id)
        {
            var course = await _courseService.GetCourseByIdAsync(id);
            if (course == null) return NotFound();

            // Tüm kayıtları al ve sadece bu kursa ait olanları filtrele
            var allEnrollments = await _enrollmentService.GetAllEnrollmentsAsync();
            var courseEnrollments = allEnrollments.Where(e => e.CourseId == id).ToList();

            // Bu kursa henüz kayıt olmamış öğrencileri bul (Sağ taraftaki 'Ekle' listesi için)
            var allStudents = await _studentService.GetAllStudentsAsync();
            var enrolledStudentIds = courseEnrollments.Select(e => e.StudentId).ToList();
            var availableStudents = allStudents.Where(s => !enrolledStudentIds.Contains(s.Id)).ToList();

            ViewBag.Course = course;
            ViewBag.AvailableStudents = availableStudents;

            return View(courseEnrollments);
        }

        // POST: Enrollment/QuickAddStudent (Manage sayfasından hızlı ekleme)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickAddStudent(int courseId, int studentId)
        {
            try
            {
                var newEnrollment = new Enrollment
                {
                    CourseId = courseId,
                    StudentId = studentId,
                    EnrollmentDate = DateTime.Now
                };

                await _enrollmentService.EnrollStudentAsync(newEnrollment);
                TempData["SuccessMessage"] = "Öğrenci sınıfa eklendi.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Hata oluştu: " + ex.Message;
            }

            return RedirectToAction(nameof(Manage), new { id = courseId });
        }

        // POST: Enrollment/QuickRemoveStudent (Manage sayfasından hızlı çıkarma)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickRemoveStudent(int enrollmentId, int courseId)
        {
            try
            {
                await _enrollmentService.RemoveEnrollmentAsync(enrollmentId);
                TempData["SuccessMessage"] = "Öğrenci sınıftan çıkarıldı.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Hata oluştu: " + ex.Message;
            }

            return RedirectToAction(nameof(Manage), new { id = courseId });
        }
    }
}