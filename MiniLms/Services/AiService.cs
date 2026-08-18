using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
        private readonly string _defaultModel;
        private readonly string _fallbackModel;
        private readonly string _embeddingModel;

        public AiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["AiServices:Gemini:ApiKey"] ?? configuration["GeminiApiKey"] ?? configuration["Gemini:ApiKey"] ?? "";

            // Modeller
            _defaultModel = configuration["AiServices:Gemini:DefaultModel"] ?? "gemini-3.5-flash";
            _fallbackModel = configuration["AiServices:Gemini:FallbackTextModel"] ?? "gemini-3.5-flash-lite";
            _embeddingModel = configuration["AiServices:Gemini:EmbeddingModel"] ?? "gemini-embedding-001";
        }

        public async Task<string> GenerateQuizAsync(string text, int questionCount = 5, string provider = "gemini", string? userApiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Quiz üretilecek metin boş.";
            string prompt = $"Aşağıdaki metne dayanarak {questionCount} adet çoktan seçmeli soru hazırla:\n\n{text}";
            return await SummarizeTextAsync(prompt, provider, userApiKey);
        }

        public async Task<List<float>?> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (!HasValidApiKey(_apiKey))
            {
                return null;
            }

            string[] embeddingModelsToTry = new[] { _embeddingModel, "gemini-embedding-001", "text-embedding-004" }
                .Distinct()
                .ToArray();

            foreach (var modelName in embeddingModelsToTry)
            {
                try
                {
                    string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:embedContent";
                    var requestBody = new { content = new { parts = new[] { new { text = text } } } };
                    string jsonPayload = JsonSerializer.Serialize(requestBody);

                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    request.Content = stringContent;
                    request.Headers.Add("x-goog-api-key", _apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
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
                    }
                }
                catch
                {
                    // Fall back to next model
                }
            }

            return null;
        }

        public async Task<string> SummarizeTextAsync(string prompt, string provider = "gemini", string? userApiKey = null)
        {
            if (string.IsNullOrEmpty(prompt)) return "Prompt içeriği boş olamaz.";

            try
            {
                if (provider == "chatgpt" && !string.IsNullOrWhiteSpace(userApiKey))
                {
                    return await CallOpenAiAsync(prompt, userApiKey);
                }
                else if (provider == "claude" && !string.IsNullOrWhiteSpace(userApiKey))
                {
                    return await CallAnthropicAsync(prompt, userApiKey);
                }

                return await CallGeminiAsync(prompt, userApiKey);
            }
            catch (Exception ex)
            {
                return $"Yapay zeka servisiyle iletişim kurulurken teknik bir hata meydana geldi: {ex.Message}";
            }
        }

        private async Task<string> CallGeminiAsync(string prompt, string? apiKey = null)
        {
            string effectiveApiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : _apiKey;
            if (!HasValidApiKey(effectiveApiKey)) return "Kurumsal Gemini API anahtarı yapılandırılmamış veya geçersiz.";

            string[] modelsToTry = new[] { _defaultModel, _fallbackModel, "gemini-3.5-flash", "gemini-3.5-flash-lite", "gemini-3-flash-preview", "gemini-3.7-flash" }
                .Distinct()
                .ToArray();

            string lastError = "";

            foreach (var modelName in modelsToTry)
            {
                try
                {
                    string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent";
                    var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
                    string jsonPayload = JsonSerializer.Serialize(requestBody);

                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    request.Content = stringContent;
                    request.Headers.Add("x-goog-api-key", effectiveApiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        return ParseGeminiResponse(await response.Content.ReadAsStringAsync());
                    }

                    string errorContent = await response.Content.ReadAsStringAsync();
                    lastError = BuildGeminiErrorMessage((int)response.StatusCode, errorContent);
                }
                catch (Exception ex)
                {
                    lastError = $"Gemini bağlantı hatası: {ex.Message}";
                }
            }

            return lastError;
        }

        private async Task<string> CallOpenAiAsync(string prompt, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[] { new { role = "user", content = prompt } }
            };

            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string rawError = await response.Content.ReadAsStringAsync();
                return $"OpenAI Reddedildi (Kod: {response.StatusCode}): Lütfen hatayı kontrol edin: {rawError}";
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "ChatGPT boş yanıt döndürdü.";
        }

        private async Task<string> CallAnthropicAsync(string prompt, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey.Trim());
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var requestBody = new
            {
                model = "claude-3-5-sonnet-20240620",
                max_tokens = 2048,
                messages = new[] { new { role = "user", content = prompt } }
            };

            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string rawError = await response.Content.ReadAsStringAsync();
                return $"Claude Reddedildi (Kod: {response.StatusCode}): {rawError}";
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);
            return document.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "Claude boş yanıt döndürdü.";
        }

        private string ParseGeminiResponse(string jsonResponse)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
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
                return "Yapay zekadan gelen JSON paketi anlamlı bir metne çözümlenemedi.";
            }
            catch
            {
                return "Yapay zeka verisi işlenirken hata oluştu.";
            }
        }

        private bool HasValidApiKey(string? key = null)
        {
            string k = key ?? _apiKey;
            return !string.IsNullOrWhiteSpace(k) &&
                   !k.Equals("apikey", StringComparison.OrdinalIgnoreCase) &&
                   !k.Equals("USE_USER_SECRETS", StringComparison.OrdinalIgnoreCase) &&
                   !k.Equals("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase) &&
                   !k.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildGeminiErrorMessage(int statusCode, string errorContent)
        {
            string message = TryReadGoogleErrorMessage(errorContent);

            if (statusCode == 400 || statusCode == 401 || statusCode == 403)
                return $"Kurumsal Gemini API anahtarı geçerli değil veya yetkisiz ({statusCode}). Detay: {message}";
            if (statusCode == 404)
                return $"Kurumsal Gemini modeli bulunamadı ({statusCode}). Detay: {message}";
            if (statusCode == 429)
                return $"Gemini API istek sınırı (Kota/Rate Limit) aşıldı ({statusCode}). Lütfen birkaç saniye sonra tekrar deneyin.";

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
            catch { }

            if (string.IsNullOrWhiteSpace(errorContent)) return "Detay alınamadı.";
            return errorContent.Length > 250 ? errorContent.Substring(0, 250) : errorContent;
        }
    }
}