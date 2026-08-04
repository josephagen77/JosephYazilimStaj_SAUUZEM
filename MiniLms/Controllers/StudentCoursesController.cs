using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.Models.Enums;
using MiniLms.ViewModels;

namespace MiniLms.Controllers
{
    [Authorize(Policy = UserPolicies.StudentOnly)]
    public class StudentCoursesController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICourseService _courseService;
        private readonly IStudentService _studentService;
        private readonly IEnrollmentService _enrollmentService;

        public StudentCoursesController(
            UserManager<ApplicationUser> userManager,
            ICourseService courseService,
            IStudentService studentService,
            IEnrollmentService enrollmentService)
        {
            _userManager = userManager;
            _courseService = courseService;
            _studentService = studentService;
            _enrollmentService = enrollmentService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var student = await GetCurrentStudentAsync();
            if (student == null)
            {
                TempData["ErrorMessage"] = "Öğrenci kaydınız bulunamadı. Lütfen öğrenci numaranızı kontrol edin.";
                return RedirectToAction("Index", "Home");
            }

            var enrollments = await _enrollmentService.GetEnrollmentsByStudentIdAsync(student.Id);
            var enrollmentList = enrollments.ToList();

            var model = new StudentCourseListViewModel
            {
                // 🎯 GÜNCELLENDİ: "Tüm Dersler" kaldırıldığı için artık veritabanından gereksiz yere çekmiyoruz
                Courses = new List<Course>(),
                Enrollments = enrollmentList,
                EnrolledCourseIds = enrollmentList.Select(e => e.CourseId).ToHashSet()
            };

            return View(model);
        }

        // Öğrencinin ders kaydını silmesi (opsiyonel olarak bıraktım)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unenroll(int courseId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null)
            {
                TempData["ErrorMessage"] = "Öğrenci kaydınız bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            var enrollments = await _enrollmentService.GetEnrollmentsByStudentIdAsync(student.Id);
            var enrollment = enrollments.FirstOrDefault(e => e.CourseId == courseId);
            if (enrollment == null)
            {
                TempData["ErrorMessage"] = "Bu derse ait size ait bir kayıt bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            await _enrollmentService.RemoveEnrollmentAsync(enrollment.Id);
            TempData["SuccessMessage"] = "Ders kaydınız silindi.";

            return RedirectToAction(nameof(Index));
        }

        private async Task<Student?> GetCurrentStudentAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (string.IsNullOrWhiteSpace(user?.StudentNumber))
            {
                return null;
            }

            return await _studentService.GetStudentByNumberAsync(user.StudentNumber);
        }
    }
}