namespace MiniLms.Models
{
    public class LessonContent
    {
        public int Id { get; set; }

        public int LessonId { get; set; }

        public Lesson Lessons { get; set; } = null!;

        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
        public int Order { get; set; }
        public bool IsIndexed { get; set; } = false;

        // 🎯 GÜNCELLENDİ: Non-nullable uyarısını çözmek için varsayılan değer atandı
        public string Type { get; set; } = string.Empty;
    }
}