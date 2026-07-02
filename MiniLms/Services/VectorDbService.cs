using MiniLms.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiniLms.Services
{
    public class VectorDbService : IVectorDbService
    {
        private readonly HttpClient _httpClient;
        private const string QdrantBaseUrl = "http://localhost:6333";

        public VectorDbService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task EnsureCollectionExistsAsync(string collectionName)
        {
            var checkResponse = await _httpClient.GetAsync($"{QdrantBaseUrl}/collections/{collectionName}");
            if (checkResponse.IsSuccessStatusCode) return; // Koleksiyon zaten var

            // Gemini embedding modeli 768 boyutludur ve mesafe ölçümü için Cosine en idealidir
            var createPayload = new
            {
                vectors = new { size = 768, distance = "Cosine" }
            };

            var content = new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json");
            await _httpClient.PutAsync($"{QdrantBaseUrl}/collections/{collectionName}", content);
        }

        public async Task SaveVectorAsync(string collectionName, int contentId, int lessonId, List<float> vector, string originalText)
        {
            await EnsureCollectionExistsAsync(collectionName);

            var uploadPayload = new
            {
                points = new[]
                {
                    new
                    {
                        id = Guid.NewGuid().ToString(), // Benzersiz nokta ID'si
                        vector = vector,
                        payload = new
                        {
                            contentId = contentId,
                            lessonId = lessonId,
                            text = originalText
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(uploadPayload), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"{QdrantBaseUrl}/collections/{collectionName}/points?wait=true", content);
        }

        public async Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> vectorData, int limit = 3)
        {
            try
            {
                // Qdrant arama endpoint'i
                string url = $"http://localhost:6333/collections/{collectionName}/points/search";

                var requestBody = new
                {
                    vector = vectorData, // Çakışmayı önlemek için 'vectorData' kullandık
                    limit = limit,
                    with_payload = true
                };

                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Qdrant Search Error]: Kod: {response.StatusCode} - Mesaj: {errorContent}");
                    return new List<string>();
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var resultList = new List<string>();

                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var point in resultProp.EnumerateArray())
                        {
                            if (point.TryGetProperty("payload", out var payloadProp) &&
                                payloadProp.TryGetProperty("text", out var textProp))
                            {
                                var text = textProp.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    resultList.Add(text);
                                }
                            }
                        }
                    }
                }

                return resultList;
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> queryVector, int lessonId, int limit = 3, List<float>? vectorData = null)
        {
            return SearchSimilarTextsAsync(collectionName, queryVector, limit);
        }
    }
}
