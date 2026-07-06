using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using MiniLms.Interfaces;
using MiniLms.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    [Authorize(Roles = "Teacher")]
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
            return View(enrollments);
        }

        // GET: Enrollment/Create
        public async Task<IActionResult> Create()
        {
            // Dropdown listelerini doldurmak için aktif öğrenci ve ders listelerini çekiyoruz
            var students = await _studentService.GetAllStudentsAsync();
            var courses = await _courseService.GetAllCoursesAsync();

            // ViewBag üzerinden arayüzdeki <select> elementlerine veri bağlıyoruz.
            // Kullanıcının daha rahat seçmesi için formatı "ÖğrenciNo - Ad Soyad" ve "DersKodu - DersAdı" haline getirdik.
            ViewBag.StudentId = new SelectList(students.Select(s => new { Id = s.Id, DisplayText = $"{s.StudentNumber} - {s.FirstName} {s.LastName}" }), "Id", "DisplayText");
            ViewBag.CourseId = new SelectList(courses.Select(c => new { Id = c.Id, DisplayText = $"{c.CourseCode} - {c.Title}" }), "Id", "DisplayText");

            return View();
        }

        // POST: Enrollment/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Enrollment enrollment)
        {
            // 1. KRİTİK DOKUNUŞ: İlişkisel 'Student' ve 'Course' nesnelerinin boş gelmesi nedeniyle 
            // ModelState.IsValid kontrolünün gizlice False'a düşmesini engellemek için bunları doğrulamadan muaf tutuyoruz.
            ModelState.Remove("Student");
            ModelState.Remove("Course");

            // 2. KRİTİK DOKUNUŞ: Kayıt tarihini formdan boş almamak adına tam butona basıldığı an atıyoruz.
            enrollment.EnrollmentDate = DateTime.Now;

            if (ModelState.IsValid)
            {
                try
                {
                    // Servis katmanında yazdığımız "Mükerrer Kayıt Kontrolü" iş kuralını tetikliyoruz
                    await _enrollmentService.EnrollStudentAsync(enrollment);
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    // "Bu öğrenci bu derse zaten kayıtlı!" hatası gelirse yakalayıp ekranın üstünde gösteriyoruz
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(string.Empty, "Ders kaydı sırasında beklenmeyen bir hata oluştu.");
                }
            }

            // Eğer validation başarısızsa veya iş kuralı hatası fırlatıldıysa dropdown listelerinin bozulmaması için yeniden dolduruyoruz
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
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                ViewBag.ErrorMessage = "Ders kaydı silinirken bir hata meydana geldi.";
                var enrollment = await _enrollmentService.GetEnrollmentByIdAsync(id);
                return View(enrollment);
            }
        }
    }
}
