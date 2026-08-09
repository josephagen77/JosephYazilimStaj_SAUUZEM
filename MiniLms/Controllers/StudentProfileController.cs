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

        public async Task<IActionResult> ApiKeys()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Kullanıcı bulunamadı."); // 🎯 YENİ: Null koruması

            var savedKeys = await _context.UserAiProviders
                .Include(u => u.AiProvider)
                .Where(u => u.UserId == user.Id)
                .ToListAsync();

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
            if (user == null) return NotFound("Kullanıcı bulunamadı."); // 🎯 YENİ: Null koruması

            var existing = await _context.UserAiProviders
                .FirstOrDefaultAsync(u => u.UserId == user.Id && u.AiProviderId == aiProviderId);

            if (existing != null)
            {
                existing.ApiKey = apiKey;
            }
            else
            {
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
            if (user == null) return NotFound("Kullanıcı bulunamadı."); // 🎯 YENİ: Null koruması

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