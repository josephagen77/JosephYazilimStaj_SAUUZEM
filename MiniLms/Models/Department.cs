using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MiniLms.Models
{
    public class Department
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty; // Fixed warning

        // The manager/head of this department
        public string? ManagerId { get; set; }
        public virtual ApplicationUser? Manager { get; set; }

        public virtual ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
        public virtual ICollection<Course> Courses { get; set; } = new List<Course>();
    }
}