using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Models;

namespace MiniLms.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentProfileController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public StudentProfileController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Öğrencinin API Anahtarlarını Yönettiği Sayfa
        public async Task<IActionResult> ApiKeys()
        {
            var user = await _userManager.GetUserAsync(User);

            // Öğrencinin daha önce kaydettiği anahtarlar
            var savedKeys = await _context.UserAiProviders
                .Include(u => u.AiProvider)
                .Where(u => u.UserId == user.Id)
                .ToListAsync();

            // Sistem Yöneticisinin AKTİF ettiği sağlayıcıları Dropdown için çek
            var activeProviders = await _context.AiProviders
                .Where(p => p.IsActive)
                .ToListAsync();

            ViewBag.AiProviders = new SelectList(activeProviders, "Id", "Name");

            return View(savedKeys);
        }

        [HttpPost]
        public async Task<IActionResult> SaveApiKey(int aiProviderId, string apiKey)
        {
            var user = await _userManager.GetUserAsync(User);

            // Bu sağlayıcı için zaten bir anahtar var mı kontrol et
            var existing = await _context.UserAiProviders
                .FirstOrDefaultAsync(u => u.UserId == user.Id && u.AiProviderId == aiProviderId);

            if (existing != null)
            {
                // Varsa güncelle
                existing.ApiKey = apiKey;
            }
            else
            {
                // Yoksa yeni ekle
                _context.UserAiProviders.Add(new UserAiProvider
                {
                    UserId = user.Id,
                    AiProviderId = aiProviderId,
                    ApiKey = apiKey
                });
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "API Anahtarınız güvenle kaydedildi.";

            return RedirectToAction(nameof(ApiKeys));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteApiKey(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var key = await _context.UserAiProviders.FirstOrDefaultAsync(k => k.Id == id && k.UserId == user.Id);

            if (key != null)
            {
                _context.UserAiProviders.Remove(key);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "API Anahtarınız sistemden silindi.";
            }

            return RedirectToAction(nameof(ApiKeys));
        }
    }
}