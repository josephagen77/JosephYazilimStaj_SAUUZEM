using System.Collections.Generic;

namespace MiniLms.Models
{
    public class Course
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string CourseCode { get; set; } = string.Empty;

        public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();

        public int Credits { get; set; }

        // 🎯 YENİ: Dersin açık veya kapalı (arşivlenmiş) olduğunu belirten özellik (Program Admin Kontrolü)
        public bool IsActive { get; set; } = true;

        // İlişki (Navigation Property)
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<CourseDocument> Documents { get; set; } = new List<CourseDocument>();
        public ICollection<LessonContent> LessonContents { get; set; } = new List<LessonContent>();

        public int? DepartmentId { get; set; }
        public virtual Department? Department { get; set; }

        public string? TeacherId { get; set; }
        public virtual ApplicationUser? Teacher { get; set; }
    }
}