using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IStudentAnalyticsService
    {
        // 🎯 Öğrencinin geçmiş sohbetlerini analiz edip Markdown raporu döndürür
        Task<string> GenerateStudentInsightAsync(string userId, int courseId);
    }
}