using Microsoft.Extensions.Configuration;
using MiniLms.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiniLms.Services
{
    public class AiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _textModel;
        private readonly string _embeddingModel;
        private readonly int _embeddingDimensions;

        public AiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            // 🎯 DÜZELTİLDİ: appsettings.json içindeki doğru hiyerarşik anahtar çağrıldı
            _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
            _textModel = configuration["Gemini:TextModel"] ?? "gemini-3.5-flash";
            _embeddingModel = configuration["Gemini:EmbeddingModel"] ?? "gemini-embedding-001";
            _embeddingDimensions = int.TryParse(configuration["Gemini:EmbeddingDimensions"], out int dimensions)
                ? dimensions
                : 768;
        }

        // 1. YAPAY ZEKA ÖZETLEME METODU
        public async Task<string> SummarizeTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Özetlenecek metin boş.";
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            {
                return "Gemini API anahtarı tanımlı değil. appsettings.json içindeki Gemini:ApiKey alanına geçerli bir Google AI Studio API anahtarı girin.";
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_textModel}:generateContent?key={_apiKey}";

                string prompt = text.Contains("ÖĞRENCİNİN SORUSU:", StringComparison.OrdinalIgnoreCase)
                    ? text
                    : $"Lütfen aşağıdaki metni akademik ve anlaşılır bir dilde özetle:\n\n{text}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Summarize Error]: {err}");
                    return BuildGeminiErrorMessage((int)response.StatusCode, err);
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    return doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();
                }
            }
            catch (Exception ex)
            {
                return $"Özet oluşturulurken teknik bir hata oluştu: {ex.Message}";
            }
        }

        // 2. YAPAY ZEKA QUIZ ÜRETME METODU
        public async Task<string> GenerateQuizAsync(string text, int questionCount = 5)
        {
            if (string.IsNullOrEmpty(text)) return "Quiz üretilecek metin boş.";
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            {
                return "Gemini API anahtarı tanımlı değil. appsettings.json içindeki Gemini:ApiKey alanına geçerli bir Google AI Studio API anahtarı girin.";
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_textModel}:generateContent?key={_apiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = $"Aşağıdaki metne dayanarak, cevapları net olan {questionCount} adet çoktan seçmeli soru hazırla:\n\n{text}" } } }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Quiz Error]: {err}");
                    return BuildGeminiErrorMessage((int)response.StatusCode, err);
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    return doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();
                }
            }
            catch (Exception ex)
            {
                return $"Quiz oluşturulurken teknik bir hata oluştu: {ex.Message}";
            }
        }

        // 3. YAPAY ZEKA EMBEDDING (VEKTÖRLEŞTİRME) METODU
        // 3. YAPAY ZEKA EMBEDDING (VEKTÖRLEŞTİRME) METODU
        public async Task<List<float>?> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[Embedding API Hatası]: Gemini API anahtarı tanımlı değil.");
                return null;
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_embeddingModel}:embedContent?key={_apiKey}";

                var requestBody = new
                {
                    taskType = "QUESTION_ANSWERING",
                    output_dimensionality = _embeddingDimensions,
                    content = new { parts = new[] { new { text = text } } }
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);

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

        private static string BuildGeminiErrorMessage(int statusCode, string errorContent)
        {
            string message = TryReadGoogleErrorMessage(errorContent);

            if (statusCode == 400 || statusCode == 401 || statusCode == 403)
            {
                return $"Gemini API isteği reddedildi ({statusCode}). API anahtarını ve Google AI Studio erişimini kontrol edin. Detay: {message}";
            }

            if (statusCode == 404)
            {
                return $"Gemini modeli bulunamadı ({statusCode}). Model adını kontrol edin. Detay: {message}";
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
                // Google hata JSON'u beklenen formatta değilse ham metni kısaltarak göster.
            }

            return string.IsNullOrWhiteSpace(errorContent)
                ? "Detay alınamadı."
                : errorContent.Length > 300 ? errorContent.Substring(0, 300) : errorContent;
        }
    }
}
