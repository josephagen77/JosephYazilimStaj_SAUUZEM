using System.ComponentModel.DataAnnotations;
using MiniLms.Models.Enums;

namespace MiniLms.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Ad zorunludur.")]
        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Soyad zorunludur.")]
        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir email giriniz.")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Öğrenci Numarası")]
        public string? StudentNumber { get; set; }

        [Required(ErrorMessage = "Rol seçimi zorunludur.")]
        public string Role { get; set; } = UserRoles.Student;

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
