using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Models;

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
            // 1. Giriş yapan yöneticiyi bul
            var user = await _userManager.GetUserAsync(User);

            if (user?.DepartmentId == null)
            {
                TempData["ErrorMessage"] = "Henüz bir departmana atanmamışsınız. Lütfen Sistem Yöneticisi ile iletişime geçin.";
                return RedirectToAction("Index", "Home");
            }

            // 2. Yöneticinin kendi departmanını veritabanından çek
            var department = await _context.Departments
                .Include(d => d.Courses)
                .Include(d => d.Users)
                .FirstOrDefaultAsync(d => d.Id == user.DepartmentId);

            if (department == null) return NotFound();

            // Sadece bu departmandaki öğretmenleri ve öğrencileri filtrele
            // Not: UserManager ile rolleri de çekebiliriz ama şimdilik hızlıca listeliyoruz
            ViewBag.TotalCourses = department.Courses?.Count ?? 0;
            ViewBag.TotalUsers = department.Users?.Count ?? 0;

            return View(department);
        }
    }
}