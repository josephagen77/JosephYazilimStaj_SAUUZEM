using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MiniLms.Interfaces;
using Microsoft.Extensions.Configuration;

namespace MiniLms.Services
{
    public class AiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _textModel;
        private readonly string _embeddingModel;

        public AiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            // appsettings.json içerisindeki Gemini:ApiKey alanını okur
            _apiKey = configuration["Gemini:ApiKey"] ?? "";
            _textModel = configuration["Gemini:TextModel"] ?? "gemini-2.5-flash";
            _embeddingModel = configuration["Gemini:EmbeddingModel"] ?? "gemini-embedding-001";
        }

        public async Task<string> GenerateQuizAsync(string text, int questionCount = 5)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Quiz üretilecek metin boş.";

            string prompt = $"Aşağıdaki metne dayanarak {questionCount} adet çoktan seçmeli soru hazırla:\n\n{text}";
            return await SummarizeTextAsync(prompt);
        }

        /// <summary>
        /// Verilen metni 768 boyutlu bir vektör dizisine (Embedding) çevirir.
        /// </summary>
        public async Task<List<float>?> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (!HasValidApiKey())
            {
                Console.WriteLine("[Embedding API Hatası]: Gemini API anahtarı geçerli görünmüyor.");
                return null;
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_embeddingModel}:embedContent";

                var requestBody = new
                {
                    content = new { parts = new[] { new { text = text } } }
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);

                // Temiz ve izole bir HTTP Request paketi oluşturuluyor
                var request = new HttpRequestMessage(HttpMethod.Post, url);

                // İçerik tipi (Content-Type) manuel olarak ezilerek local çerezlerin sızması engelleniyor
                var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                stringContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                request.Content = stringContent;
                request.Headers.Add("x-goog-api-key", _apiKey);

                // HttpClient fabrikasından gelebilecek tüm kirli header geçmişini sıfırlıyoruz
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Embedding API Hatası]: Kod: {response.StatusCode} - Mesaj: {errorContent}");
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();

                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("embedding", out var embeddingProp) &&
                        embeddingProp.TryGetProperty("values", out var valuesProp))
                    {
                        var embeddingResult = new List<float>();
                        foreach (var val in valuesProp.EnumerateArray())
                        {
                            embeddingResult.Add(val.GetSingle());
                        }
                        return embeddingResult;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Embedding Exception]: Hata Detayı: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Hazırlanan RAG promptunu Gemini modeline göndererek bağlamsal cevap veya özet üretir.
        /// </summary>
        public async Task<string> SummarizeTextAsync(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return "Prompt içeriği boş olamaz.";
            if (!HasValidApiKey())
            {
                return "Gemini API anahtarı geçerli değil. Google AI Studio'dan alınan API key'i appsettings.json içindeki Gemini:ApiKey alanına ekleyin.";
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_textModel}:generateContent";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);

                // İstek gövdesi tamamen sterilize ediliyor
                var request = new HttpRequestMessage(HttpMethod.Post, url);

                var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                stringContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                request.Content = stringContent;
                request.Headers.Add("x-goog-api-key", _apiKey);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    return BuildGeminiErrorMessage((int)response.StatusCode, errorContent);
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();

                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var contentProp) &&
                            contentProp.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                        {
                            return parts[0].GetProperty("text").GetString() ?? "Asistandan boş içerik döndü.";
                        }
                    }
                }

                return "Yapay zekadan gelen JSON paketi anlamlı bir metne çözümlenemedi.";
            }
            catch (Exception ex)
            {
                return $"Yapay zeka servisiyle iletişim kurulurken teknik bir hata meydana geldi: {ex.Message}";
            }
        }

        private bool HasValidApiKey()
        {
            return !string.IsNullOrWhiteSpace(_apiKey) &&
                   !_apiKey.Equals("apikey", StringComparison.OrdinalIgnoreCase) &&
                   !_apiKey.Equals("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase) &&
                   !_apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                   (_apiKey.StartsWith("AIza", StringComparison.Ordinal) ||
                    _apiKey.StartsWith("AQ.", StringComparison.Ordinal));
        }

        private static string BuildGeminiErrorMessage(int statusCode, string errorContent)
        {
            string message = TryReadGoogleErrorMessage(errorContent);

            if (statusCode == 400 || statusCode == 401 || statusCode == 403)
            {
                return $"Gemini API anahtarı geçerli değil veya bu projede yetkili değil ({statusCode}). Google AI Studio'dan yeni bir API key oluşturup appsettings.json içindeki Gemini:ApiKey alanına ekleyin. Detay: {message}";
            }

            if (statusCode == 404)
            {
                return $"Gemini modeli bulunamadı ({statusCode}). appsettings.json içindeki Gemini:TextModel değerini kontrol edin. Detay: {message}";
            }

            return $"Gemini API şu an yanıt üretemedi ({statusCode}). Detay: {message}";
        }

        private static string TryReadGoogleErrorMessage(string errorContent)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(errorContent);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? "Detay alınamadı.";
                }
            }
            catch
            {
                // Google hata cevabı JSON değilse ham metni kısaltarak göster.
            }

            if (string.IsNullOrWhiteSpace(errorContent))
            {
                return "Detay alınamadı.";
            }

            return errorContent.Length > 250
                ? errorContent.Substring(0, 250)
                : errorContent;
        }
    }
}
