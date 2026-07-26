using System.ComponentModel.DataAnnotations;

namespace MiniLms.ViewModels
{
    public class StudentEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ad zorunludur.")]
        [StringLength(50, ErrorMessage = "Ad en fazla 50 karakter olabilir.")]
        public required string FirstName { get; set; }

        [Required(ErrorMessage = "Soyad zorunludur.")]
        [StringLength(50, ErrorMessage = "Soyad en fazla 50 karakter olabilir.")]
        public required string LastName { get; set; }

        [Required(ErrorMessage = "Email zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir email giriniz.")]
        public required string Email { get; set; }

        [Required(ErrorMessage = "Öğrenci numarası zorunludur.")]
        public required string StudentNumber { get; set; }
    }
}