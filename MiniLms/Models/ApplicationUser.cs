using Microsoft.AspNetCore.Identity;

namespace MiniLms.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        
        public string? StudentNumber { get; set; }

        // Add these inside your ApplicationUser class:
        public int? DepartmentId { get; set; }
        public virtual Department? Department { get; set; }

        public virtual ICollection<UserAiProvider> SavedAiKeys { get; set; } = new List<UserAiProvider>();
    }
}
