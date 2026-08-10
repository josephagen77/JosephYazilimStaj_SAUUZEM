using System.Collections.Generic;

namespace MiniLms.Models
{
    public class Course
    {
        public int Id { get; set; }

        // Hatanın kaynağı burasıydı; kesinlikle 'string' olmalı
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string CourseCode { get; set; } = string.Empty;
        // Models/Course.cs içerisindeki ilişkisel liste tanımı tam olarak böyle olmalı:
        public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
        public int Credits { get; set; }

        // İlişki (Navigation Property)
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<CourseDocument> Documents { get; set; } = new List<CourseDocument>();
       
        public ICollection<LessonContent> LessonContents { get; set; } = new List<LessonContent>();
        // Add these inside your Course class:
        public int? DepartmentId { get; set; }
        public virtual Department? Department { get; set; }
        public object Teacher { get; internal set; }
        public string? TeacherId { get; internal set; }
    }
}