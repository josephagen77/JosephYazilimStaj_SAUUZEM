using System.ComponentModel.DataAnnotations;

namespace MiniLms.Models
{
    public class UserAiProvider
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty; // Fixed warning
        public virtual ApplicationUser User { get; set; } = null!; // Fixed warning

        [Required]
        public int AiProviderId { get; set; }
        public virtual AiProvider AiProvider { get; set; } = null!; // Fixed warning

        [Required]
        public string ApiKey { get; set; } = string.Empty; // Fixed warning
    }
}