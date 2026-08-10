using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniLms.Models;

namespace MiniLms.Data
{
    public static class DbInitializer
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Temel Rolleri Oluştur
            string[] roleNames = { "SystemAdmin", "ProgramManager", "Teacher", "Student" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // 2. Varsayılan Sistem Yöneticisi Hesabını Oluştur
            string adminEmail = "admin@minilms.com";
            string adminPassword = "Password123*";

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "Sistem",
                    LastName = "Yöneticisi",
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "SystemAdmin");
                }
            }

            // 3. Varsayılan Gemini AI Modeli Ekle (Sistem Yöneticisi Kapatıp Açabilsin)
            var geminiProvider = await context.AiProviders.FirstOrDefaultAsync(p => p.ProviderKey == "gemini");
            if (geminiProvider == null)
            {
                context.AiProviders.Add(new AiProvider
                {
                    Name = "Google Gemini (Varsayılan Kurumsal Mod)",
                    ProviderKey = "gemini",
                    IsActive = true,
                    GlobalApiKey = null // Null olduğu için User Secrets'tan çekecek
                });
                await context.SaveChangesAsync();
            }
        }
    }
}