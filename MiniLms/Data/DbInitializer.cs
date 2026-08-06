using Microsoft.AspNetCore.Identity;
using MiniLms.Models; // Kendi proje adınıza göre ayarlayın

namespace MiniLms.Data
{
    public static class DbInitializer
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            // UserManager ve RoleManager servislerini çağırıyoruz
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // 1. Sistemdeki Temel Rolleri Oluştur
            string[] roleNames = { "SystemAdmin", "ProgramManager", "Teacher", "Student" };

            foreach (var roleName in roleNames)
            {
                // Eğer rol yoksa, veritabanına ekle
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // 2. Varsayılan Sistem Yöneticisi Hesabını Oluştur
            string adminEmail = "admin@minilms.com";
            string adminPassword = "Password123*"; // Güçlü bir şifre olmalı

            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null) // Eğer admin hesabı yoksa, sıfırdan oluştur
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "Sistem",
                    LastName = "Yöneticisi",
                    EmailConfirmed = true // E-posta onayını direkt true yapıyoruz
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);

                if (result.Succeeded)
                {
                    // Oluşturulan hesaba "SystemAdmin" yetkisini ver
                    await userManager.AddToRoleAsync(adminUser, "SystemAdmin");
                }
            }
        }
    }
}