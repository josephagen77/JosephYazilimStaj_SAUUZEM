using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Models;

namespace MiniLms.Controllers
{
    // Yalnızca Sistem Yöneticisi girebilir
    [Authorize(Roles = "SystemAdmin")]
    public class SystemAdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SystemAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        // --- AI SAĞLAYICI YÖNETİMİ ---
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

        // 🎯 YENİ: Yapay Zeka Sağlayıcısını Düzenleme Metodu
        [HttpPost]
        public async Task<IActionResult> EditAiProvider(int id, string name, string providerKey, string? globalApiKey)
        {
            var provider = await _context.AiProviders.FindAsync(id);
            if (provider == null)
            {
                return NotFound();
            }

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
    }
}