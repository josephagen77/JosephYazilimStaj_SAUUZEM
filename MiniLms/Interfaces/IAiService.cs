using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IAiService
    {
        // Doküman özetleme metodu (Now supports dynamic model selection)
        Task<string> SummarizeTextAsync(string text, string? modelName = null);

        // Dokümandan test/quiz üretme metodu (Now supports dynamic model selection)
        Task<string> GenerateQuizAsync(string text, int questionCount = 5, string? modelName = null);

        // Vector DB (Qdrant) için metinleri 768 boyutlu vektöre çeviren metot
        Task<List<float>?> GetEmbeddingAsync(string text);
    }
}