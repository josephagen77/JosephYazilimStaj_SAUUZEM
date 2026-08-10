using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Models;

namespace MiniLms.Controllers
{
    [Authorize(Roles = "SystemAdmin")]
    public class SystemAdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public SystemAdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public IActionResult Index()
        {
            ViewBag.TotalUsers = _userManager.Users.Count();
            ViewBag.TotalDepartments = _context.Departments.Count();
            ViewBag.TotalAiProviders = _context.AiProviders.Count();
            ViewBag.TotalCourses = _context.Courses.Count();
            return View();
        }

        // ==========================================
        // 1. AI SAĞLAYICI YÖNETİMİ
        // ==========================================
        public async Task<IActionResult> AiProviders()
        {
            var providers = await _context.AiProviders.ToListAsync();
            return View(providers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAiProvider(string name, string providerKey, string? globalApiKey)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(providerKey))
            {
                TempData["ErrorMessage"] = "AI Adı ve Sağlayıcı Anahtarı zorunludur.";
                return RedirectToAction(nameof(AiProviders));
            }

            var newProvider = new AiProvider
            {
                Name = name,
                ProviderKey = providerKey.ToLower().Trim(),
                GlobalApiKey = globalApiKey,
                IsActive = true
            };
            _context.AiProviders.Add(newProvider);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"{name} başarıyla sisteme eklendi!";
            return RedirectToAction(nameof(AiProviders));
        }

        [HttpPost]
        public async Task<IActionResult> EditAiProvider(int id, string name, string providerKey, string? globalApiKey)
        {
            var provider = await _context.AiProviders.FindAsync(id);
            if (provider == null) return NotFound();

            provider.Name = name;
            provider.ProviderKey = providerKey.ToLower().Trim();
            provider.GlobalApiKey = globalApiKey;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"{name} başarıyla güncellendi!";
            return RedirectToAction(nameof(AiProviders));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleAiProvider(int id)
        {
            var provider = await _context.AiProviders.FindAsync(id);
            if (provider == null) return NotFound();
            provider.IsActive = !provider.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"{provider.Name} durumu güncellendi.";
            return RedirectToAction(nameof(AiProviders));
        }

        // ==========================================
        // 2. KULLANICI (USER) YÖNETİMİ (CRUD)
        // ==========================================
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.Include(u => u.Department).ToListAsync();
            ViewBag.Departments = new SelectList(await _context.Departments.ToListAsync(), "Id", "Name");
            ViewBag.Roles = new SelectList(await _roleManager.Roles.Select(r => r.Name).ToListAsync());
            return View(users);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser(string firstName, string lastName, string email, string password, string role, int? departmentId)
        {
            if (await _userManager.FindByEmailAsync(email) != null)
            {
                TempData["ErrorMessage"] = "Bu e-posta adresi zaten kullanımda.";
                return RedirectToAction(nameof(Users));
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                DepartmentId = departmentId,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(role))
                {
                    await _userManager.AddToRoleAsync(user, role);
                }
                TempData["SuccessMessage"] = $"{firstName} {lastName} sisteme eklendi.";
            }
            else
            {
                TempData["ErrorMessage"] = "Kullanıcı oluşturulurken hata: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public async Task<IActionResult> EditUser(string id, string firstName, string lastName, string email, string role, int? departmentId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            user.FirstName = firstName;
            user.LastName = lastName;
            user.Email = email;
            user.UserName = email;
            user.DepartmentId = departmentId;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!string.IsNullOrEmpty(role))
                {
                    await _userManager.AddToRoleAsync(user, role);
                }
                TempData["SuccessMessage"] = $"{firstName} {lastName} bilgileri güncellendi.";
            }
            else
            {
                TempData["ErrorMessage"] = "Güncelleme hatası: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                if (user.Id == _userManager.GetUserId(User))
                {
                    TempData["ErrorMessage"] = "Kendi hesabınızı silemezsiniz!";
                    return RedirectToAction(nameof(Users));
                }

                await _userManager.DeleteAsync(user);
                TempData["SuccessMessage"] = "Kullanıcı başarıyla silindi.";
            }
            return RedirectToAction(nameof(Users));
        }

        // ==========================================
        // 3. DEPARTMAN YÖNETİMİ (CRUD)
        // ==========================================
        public async Task<IActionResult> Departments()
        {
            var departments = await _context.Departments.Include(d => d.Manager).Include(d => d.Users).Include(d => d.Courses).ToListAsync();
            var managers = await _userManager.GetUsersInRoleAsync("ProgramManager");
            ViewBag.Managers = new SelectList(managers, "Id", "Email");
            return View(departments);
        }

        [HttpPost]
        public async Task<IActionResult> CreateDepartment(string name, string? managerId)
        {
            var dept = new Department { Name = name, ManagerId = managerId };
            _context.Departments.Add(dept);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Departman oluşturuldu.";
            return RedirectToAction(nameof(Departments));
        }

        [HttpPost]
        public async Task<IActionResult> EditDepartment(int id, string name, string? managerId)
        {
            var dept = await _context.Departments.FindAsync(id);
            if (dept == null) return NotFound();

            dept.Name = name;
            dept.ManagerId = string.IsNullOrEmpty(managerId) ? null : managerId;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Departman bilgileri güncellendi.";
            return RedirectToAction(nameof(Departments));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            var dept = await _context.Departments.FindAsync(id);
            if (dept != null)
            {
                _context.Departments.Remove(dept);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Departman silindi.";
            }
            return RedirectToAction(nameof(Departments));
        }

        // ==========================================
        // 4. DERS (COURSE) YÖNETİMİ (CRUD) 🎯 YENİ
        // ==========================================
        public async Task<IActionResult> Courses()
        {
            var courses = await _context.Courses
                .Include(c => c.Department)
                .Include(c => c.Teacher)
                .Include(c => c.Lessons)
                .ToListAsync();

            var teachers = await _userManager.GetUsersInRoleAsync("Teacher");
            ViewBag.Teachers = new SelectList(teachers, "Id", "Email");
            ViewBag.Departments = new SelectList(await _context.Departments.ToListAsync(), "Id", "Name");

            return View(courses);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCourse(string title, string courseCode, string description, int credits, int? departmentId, string? teacherId)
        {
            var course = new Course
            {
                Title = title,
                CourseCode = courseCode,
                Description = description,
                Credits = credits,
                DepartmentId = departmentId,
                TeacherId = string.IsNullOrEmpty(teacherId) ? null : teacherId
            };

            _context.Courses.Add(course);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"{title} dersi sisteme eklendi.";
            return RedirectToAction(nameof(Courses));
        }

        [HttpPost]
        public async Task<IActionResult> EditCourse(int id, string title, string courseCode, string description, int credits, int? departmentId, string? teacherId)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null) return NotFound();

            course.Title = title;
            course.CourseCode = courseCode;
            course.Description = description;
            course.Credits = credits;
            course.DepartmentId = departmentId;
            course.TeacherId = string.IsNullOrEmpty(teacherId) ? null : teacherId;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"{title} dersi güncellendi.";
            return RedirectToAction(nameof(Courses));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course != null)
            {
                _context.Courses.Remove(course);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Ders sistemden silindi.";
            }
            return RedirectToAction(nameof(Courses));
        }
    }
}