using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IAiService
    {
        // Doküman özetleme ve genel sohbet metodu (Çoklu sağlayıcı destekler)
        Task<string> SummarizeTextAsync(string text, string provider = "gemini", string? userApiKey = null);

        // Dokümandan test/quiz üretme metodu (Çoklu sağlayıcı destekler)
        Task<string> GenerateQuizAsync(string text, int questionCount = 5, string provider = "gemini", string? userApiKey = null);

        // Vector DB (Qdrant) için metinleri vektöre çeviren metot (Her zaman okulun güvenli Gemini'sini kullanır)
        Task<List<float>?> GetEmbeddingAsync(string text);
    }
}