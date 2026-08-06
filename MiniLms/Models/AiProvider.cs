using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MiniLms.Models
{
    public class AiProvider
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty; // Fixed warning

        [Required]
        [StringLength(50)]
        public string ProviderKey { get; set; } = string.Empty; // Fixed warning

        public bool IsActive { get; set; } = true;

        public string? GlobalApiKey { get; set; }

        public virtual ICollection<UserAiProvider> UserAiProviders { get; set; } = new List<UserAiProvider>();
    }
}