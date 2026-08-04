using MiniLms.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniLms.Services
{
    public class VectorDbService : IVectorDbService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VectorDbService> _logger;

        private const string QdrantBaseUrl = "http://localhost:6333";
        private const string CourseCollectionName = "course_vectors"; // 🎯 Kurslar için özel tablo

        public VectorDbService(HttpClient httpClient, ILogger<VectorDbService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task EnsureCollectionExistsAsync(string collectionName)
        {
            var checkResponse = await _httpClient.GetAsync($"{QdrantBaseUrl}/collections/{collectionName}");
            if (checkResponse.IsSuccessStatusCode) return;

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
                        id = Guid.NewGuid().ToString(),
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

        // 🎯 YENİ: Kurs verilerini Qdrant'a kaydet (CourseId ile birlikte)
        public async Task SaveCourseVectorAsync(int courseId, List<float> vector, string courseData)
        {
            await EnsureCollectionExistsAsync(CourseCollectionName);

            var uploadPayload = new
            {
                points = new[]
                {
                    new
                    {
                        // Qdrant'ın int ID kabul etmesi için CourseId'yi dönüştürüyoruz
                        id = courseId,
                        vector = vector,
                        payload = new
                        {
                            courseId = courseId,
                            searchData = courseData // Özet veri (başlık + açıklama)
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(uploadPayload), Encoding.UTF8, "application/json");
            await _httpClient.PutAsync($"{QdrantBaseUrl}/collections/{CourseCollectionName}/points?wait=true", content);
        }

        public async Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> vectorData, int limit = 3)
        {
            try
            {
                string url = $"{QdrantBaseUrl}/collections/{collectionName}/points/search";

                var requestBody = new
                {
                    vector = vectorData,
                    limit = limit,
                    with_payload = true
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode) return new List<string>();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var resultList = new List<string>();

                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Array)
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant text search failed.");
                return new List<string>();
            }
        }

        // 🎯 YENİ: Anlamsal olarak aranan Kurs ID'lerini geri döndür
        public async Task<List<int>> SearchSimilarCoursesAsync(List<float> queryVector, int limit = 5)
        {
            try
            {
                string url = $"{QdrantBaseUrl}/collections/{CourseCollectionName}/points/search";

                var requestBody = new
                {
                    vector = queryVector,
                    limit = limit,
                    with_payload = true
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode) return new List<int>();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var matchingCourseIds = new List<int>();

                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    // Skoru belli bir eşiğin üstünde olanları alıyoruz (örn. 0.60 Cosine Similarity)
                    if (root.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var point in resultProp.EnumerateArray())
                        {
                            if (point.TryGetProperty("score", out var scoreProp) && scoreProp.GetDouble() > 0.55)
                            {
                                if (point.TryGetProperty("payload", out var payloadProp) &&
                                    payloadProp.TryGetProperty("courseId", out var idProp))
                                {
                                    matchingCourseIds.Add(idProp.GetInt32());
                                }
                            }
                        }
                    }
                }
                return matchingCourseIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant course search failed.");
                return new List<int>();
            }
        }

        public Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> queryVector, int lessonId, int limit = 3, List<float>? vectorData = null)
        {
            return SearchSimilarTextsAsync(collectionName, queryVector, limit);
        }

        public async Task<bool> DeleteVectorAsync(string pointId)
        {
            try
            {
                // Qdrant delete requires a POST payload specifying the points to delete
                string url = $"{QdrantBaseUrl}/collections/lesson_contents/points/delete";
                var payload = new { points = new[] { pointId } };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant string ID delete failed.");
                return false;
            }
        }

        public async Task<bool> DeleteVectorAsync(List<long> pointIds)
        {
            try
            {
                string url = $"{QdrantBaseUrl}/collections/lesson_contents/points/delete";
                var payload = new { points = pointIds };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant numeric IDs delete failed.");
                return false;
            }
        }
    }
}