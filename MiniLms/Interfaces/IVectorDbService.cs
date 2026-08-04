using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IVectorDbService
    {
        // Qdrant'ta "lesson_contents" veya "course_vectors" adında bir alan yoksa otomatik oluşturur
        Task EnsureCollectionExistsAsync(string collectionName);

        // Vektörleştirilmiş içeriği Qdrant'a kaydeder (Ders içerikleri için)
        Task SaveVectorAsync(string collectionName, int contentId, int lessonId, List<float> vector, string originalText);

        // Kullanıcının sorusuna anlamsal olarak en yakın metinleri getirir
        Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> queryVector, int lessonId, int limit = 3, List<float>? vectorData = null);
        Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> vectorData, int limit);

        Task<bool> DeleteVectorAsync(List<long> pointIds);
        Task<bool> DeleteVectorAsync(string pointId);

        // 🎯 YENİ: Kurs Keşfi (Semantic Search) için metodlar
        Task SaveCourseVectorAsync(int courseId, List<float> vector, string courseData);
        Task<List<int>> SearchSimilarCoursesAsync(List<float> queryVector, int limit = 5);
    }
}