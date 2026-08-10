using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Models;

namespace MiniLms.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public ProfileController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Kullanıcı oturumu bulunamadı.");

            var userWithDept = await _context.Users
                .Include(u => u.Department)
                .FirstOrDefaultAsync(u => u.Id == user.Id);

            var roles = await _userManager.GetRolesAsync(user);
            ViewBag.Role = roles.FirstOrDefault() ?? "Atanmadı";

            return View(userWithDept ?? user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string firstName, string lastName)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            user.FirstName = firstName;
            user.LastName = lastName;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Profil bilgileriniz başarıyla güncellendi.";
            }
            else
            {
                TempData["ErrorMessage"] = "Güncelleme sırasında bir hata oluştu.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}