using System.ComponentModel.DataAnnotations;

namespace MiniLms.ViewModels
{
    public class LessonViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ders başlığı zorunludur.")]
        [StringLength(100, ErrorMessage = "Ders başlığı en fazla 100 karakter olabilir.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir.")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Ders sırası zorunludur.")]
        [Range(1, 1000, ErrorMessage = "Ders sırası 1 ile 1000 arasında olmalıdır.")]
        public int Order { get; set; }

        public int CourseId { get; set; }
    }
}