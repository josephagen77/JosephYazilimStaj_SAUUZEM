using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.Models.Enums;
using MiniLms.ViewModels;

namespace MiniLms.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IStudentService _studentService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IStudentService studentService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _studentService = studentService;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }

                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user != null && await _userManager.IsInRoleAsync(user, UserRoles.Student))
                {
                    return RedirectToAction("Index", "StudentCourses");
                }

                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "Email veya şifre hatalı.");
            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (model.Role != UserRoles.Student && model.Role != UserRoles.Teacher)
            {
                ModelState.AddModelError(nameof(model.Role), "Geçersiz rol seçimi.");
            }

            if (model.Role == UserRoles.Student && string.IsNullOrWhiteSpace(model.StudentNumber))
            {
                ModelState.AddModelError(nameof(model.StudentNumber), "Öğrenci rolü için öğrenci numarası zorunludur.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.Role == UserRoles.Student)
            {
                var studentNumberInUse = await _userManager.Users
                    .AnyAsync(u => u.StudentNumber == model.StudentNumber);

                if (studentNumberInUse)
                {
                    ModelState.AddModelError(nameof(model.StudentNumber), "Bu öğrenci numarasıyla zaten bir kullanıcı kaydı var.");
                    return View(model);
                }
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                StudentNumber = model.Role == UserRoles.Student ? model.StudentNumber : null
            };

            var createResult = await _userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            await _userManager.AddToRoleAsync(user, model.Role);

            if (model.Role == UserRoles.Student)
            {
                var existingStudent = await _studentService.GetStudentByNumberAsync(model.StudentNumber!);
                if (existingStudent == null)
                {
                    await _studentService.AddStudentAsync(new StudentCreateViewModel
                    {
                        FirstName = model.FirstName,
                        LastName = model.LastName,
                        Email = model.Email,
                        StudentNumber = model.StudentNumber!
                    });
                }
            }

            await _signInManager.SignInAsync(user, isPersistent: false);

            if (model.Role == UserRoles.Student)
            {
                return RedirectToAction("Index", "StudentCourses");
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
