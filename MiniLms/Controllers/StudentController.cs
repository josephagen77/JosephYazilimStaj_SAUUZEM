using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MiniLms.Interfaces;
using MiniLms.Models.Enums;
using MiniLms.ViewModels;
using System;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    [Authorize(Policy = UserPolicies.TeacherOnly)]
    public class StudentController : Controller
    {
        private readonly IStudentService _studentService;
        private readonly IMapper _mapper;

        public StudentController(IStudentService studentService, IMapper mapper)
        {
            _studentService = studentService;
            _mapper = mapper;
        }

        // GET: Student
        public async Task<IActionResult> Index()
        {
            // Servisten tüm öğrencileri asenkron olarak listeliyoruz
            var students = await _studentService.GetAllStudentsAsync();
            return View(students);
        }

        // GET: Student/Details/5
        public async Task<IActionResult> Details(int id)
        {
            // Detay sayfasında öğrencinin aldığı dersleri de listelemek için 
            // ilişkisel sorgu metodumuzu çağırıyoruz.
            var student = await _studentService.GetStudentWithEnrollmentsAsync(id);
            if (student == null)
            {
                return NotFound();
            }

            return View(student);
        }

        // GET: Student/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Student/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(StudentCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Arayüzden gelen ViewModel verisini servise gönderiyoruz
                await _studentService.AddStudentAsync(model);
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Servisten fırlatılan iş kuralı hatasını yakalayıp ekrana basıyoruz
                ModelState.AddModelError("StudentNumber", ex.Message);
                return View(model);
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "Öğrenci kaydedilirken beklenmeyen bir hata oluştu.");
                return View(model);
            }
        }

        // GET: Student/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var student = await _studentService.GetStudentByIdAsync(id);
            if (student == null)
            {
                return NotFound();
            }

            // REVERSEMAP KULLANIMI: Veritabanı modelini (Student), düzenleme formunun
            // beklediği model tipine (StudentEditViewModel) dönüştürüyoruz.
            var model = _mapper.Map<StudentEditViewModel>(student);
            return View(model);
        }

        // POST: Student/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, StudentEditViewModel model)
        {
            if (id != model.Id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                await _studentService.UpdateStudentAsync(model);
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("StudentNumber", ex.Message);
                return View(model);
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "Güncelleme işlemi sırasında bir hata oluştu.");
                return View(model);
            }
        }

        // GET: Student/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var student = await _studentService.GetStudentByIdAsync(id);
            if (student == null)
            {
                return NotFound();
            }

            return View(student);
        }

        // POST: Student/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _studentService.DeleteStudentAsync(id);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                ViewBag.ErrorMessage = "Öğrenci silinirken bir hata meydana geldi.";
                var student = await _studentService.GetStudentByIdAsync(id);
                return View(student);
            }
        }
    }
}
