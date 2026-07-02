using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IAiService
    {
        // Doküman özetleme metodu
        Task<string> SummarizeTextAsync(string text);

        // Dokümandan test/quiz üretme metodu
        Task<string> GenerateQuizAsync(string text, int questionCount = 5);

        // Vector DB (Qdrant) için metinleri 768 boyutlu vektöre çeviren yeni metot
        Task<List<float>?> GetEmbeddingAsync(string text);
    }
}
