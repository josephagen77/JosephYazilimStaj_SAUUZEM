using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    // Sadece Program Yöneticileri girebilir
    [Authorize(Roles = "ProgramManager")]
    public class ProgramManagerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProgramManagerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user?.DepartmentId == null)
            {
                TempData["ErrorMessage"] = "Henüz bir departmana atanmamışsınız. Lütfen Sistem Yöneticisi ile iletişime geçin.";
                return RedirectToAction("Index", "Home");
            }

            var department = await _context.Departments
                .Include(d => d.Courses)
                    .ThenInclude(c => c.Teacher)
                .Include(d => d.Users)
                .FirstOrDefaultAsync(d => d.Id == user.DepartmentId);

            if (department == null) return NotFound();

            var teachers = new List<ApplicationUser>();
            var students = new List<ApplicationUser>();

            if (department.Users != null)
            {
                foreach (var u in department.Users)
                {
                    if (await _userManager.IsInRoleAsync(u, "Teacher"))
                    {
                        teachers.Add(u);
                    }
                    else if (await _userManager.IsInRoleAsync(u, "Student"))
                    {
                        students.Add(u);
                    }
                }
            }

            ViewBag.Teachers = teachers;
            ViewBag.Students = students;
            ViewBag.TotalCourses = department.Courses?.Count ?? 0;
            ViewBag.TotalUsers = department.Users?.Count ?? 0;

            return View(department);
        }

        #region --- DERS (COURSE) YÖNETİMİ ---

        [HttpPost]
        public async Task<IActionResult> AddCourse(string title, string courseCode, int credits)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user?.DepartmentId == null) return RedirectToAction(nameof(Index));

            var newCourse = new Course
            {
                Title = title,
                CourseCode = courseCode,
                Credits = credits,
                DepartmentId = user.DepartmentId.Value,
                IsActive = true
            };

            _context.Courses.Add(newCourse);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Yeni ders başarıyla eklendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> EditCourse(int courseId, string title, string courseCode, int credits)
        {
            var course = await _context.Courses.FindAsync(courseId);
            if (course != null)
            {
                course.Title = title;
                course.CourseCode = courseCode;
                course.Credits = credits;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Ders bilgileri güncellendi.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCourse(int courseId)
        {
            var course = await _context.Courses.FindAsync(courseId);
            if (course != null)
            {
                _context.Courses.Remove(course);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Ders tamamen silindi.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleCourseStatus(int courseId)
        {
            var course = await _context.Courses.FindAsync(courseId);
            if (course != null)
            {
                course.IsActive = !course.IsActive;
                await _context.SaveChangesAsync();
                string status = course.IsActive ? "Öğrencilere Açıldı" : "Kapatıldı (Arşivlendi)";
                TempData["SuccessMessage"] = $"'{course.Title}' dersi başarıyla {status}.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> AssignTeacher(int courseId, string? teacherId)
        {
            var course = await _context.Courses.FindAsync(courseId);
            if (course != null)
            {
                course.TeacherId = string.IsNullOrEmpty(teacherId) ? null : teacherId;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Eğitmen ataması başarıyla güncellendi.";
            }
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region --- KULLANICI (ÖĞRETMEN / ÖĞRENCİ) YÖNETİMİ ---

        [HttpPost]
        public async Task<IActionResult> AddUser(string firstName, string lastName, string email, string password, string role, string? studentNumber)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.DepartmentId == null) return RedirectToAction(nameof(Index));

            var newUser = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                DepartmentId = currentUser.DepartmentId,
                EmailConfirmed = true
            };

            // Eğer öğrenciyse, öğrenci numarasını ata
            if (role == "Student" && !string.IsNullOrWhiteSpace(studentNumber))
            {
                newUser.StudentNumber = studentNumber;
            }

            var result = await _userManager.CreateAsync(newUser, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(newUser, role);

                if (role == "Student" && !string.IsNullOrWhiteSpace(studentNumber))
                {
                    bool studentExists = await _context.Students.AnyAsync(s => s.StudentNumber == studentNumber);
                    if (!studentExists)
                    {
                        _context.Students.Add(new Student
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            StudentNumber = studentNumber
                        });
                        await _context.SaveChangesAsync();
                    }
                }

                string roleName = role == "Teacher" ? "Eğitmen" : "Öğrenci";
                TempData["SuccessMessage"] = $"{firstName} {lastName} başarıyla {roleName} olarak eklendi.";
            }
            else
            {
                TempData["ErrorMessage"] = "Kullanıcı eklenirken hata: " + string.Join(" | ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> EditUser(string userId, string firstName, string lastName, string email, string? studentNumber)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.FirstName = firstName;
                user.LastName = lastName;
                user.Email = email;
                user.UserName = email;

                // Eğer öğrenciyse ve öğrenci numarası gönderildiyse güncelle
                if (await _userManager.IsInRoleAsync(user, "Student"))
                {
                    user.StudentNumber = studentNumber;
                }

                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = "Kullanıcı bilgileri başarıyla güncellendi.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var courses = await _context.Courses.Where(c => c.TeacherId == userId).ToListAsync();
                foreach (var course in courses) course.TeacherId = null;
                await _context.SaveChangesAsync();

                await _userManager.DeleteAsync(user);
                TempData["SuccessMessage"] = "Kullanıcı başarıyla sistemden silindi.";
            }
            return RedirectToAction(nameof(Index));
        }

        #endregion
    }
}