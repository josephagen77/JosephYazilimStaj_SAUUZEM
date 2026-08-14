using MiniLms.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MiniLms.Services
{
    public class VectorDbService : IVectorDbService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VectorDbService> _logger;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        private const string CourseCollectionName = "course_vectors";

        public VectorDbService(HttpClient httpClient, IConfiguration configuration, ILogger<VectorDbService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["AiServices:Qdrant:ApiKey"] ?? string.Empty;
            _baseUrl = configuration["AiServices:Qdrant:BaseUrl"] ?? string.Empty;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (content != null)
            {
                request.Content = content;
            }
            return request;
        }

        public async Task EnsureCollectionExistsAsync(string collectionName)
        {
            try
            {
                using var checkRequest = CreateRequest(HttpMethod.Get, $"collections/{collectionName}");
                var checkResponse = await _httpClient.SendAsync(checkRequest);
                if (checkResponse.IsSuccessStatusCode) return;

                // Gemini embedding modeli 768 boyutludur ve mesafe ölçümü için Cosine en idealidir
                var createPayload = new
                {
                    vectors = new { size = 768, distance = "Cosine" }
                };

                var content = new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json");
                using var createRequest = CreateRequest(HttpMethod.Put, $"collections/{collectionName}", content);
                var createResponse = await _httpClient.SendAsync(createRequest);

                if (!createResponse.IsSuccessStatusCode)
                {
                    string err = await createResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant collection '{CollectionName}' creation returned {StatusCode}: {Error}", collectionName, createResponse.StatusCode, err);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure Qdrant collection '{CollectionName}' exists.", collectionName);
            }
        }

        public async Task SaveVectorAsync(string collectionName, int contentId, int lessonId, List<float> vector, string originalText)
        {
            try
            {
                await EnsureCollectionExistsAsync(collectionName);

                var uploadPayload = new
                {
                    points = new[]
                    {
                        new
                        {
                            id = (long)contentId,
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
                using var request = CreateRequest(HttpMethod.Put, $"collections/{collectionName}/points?wait=true", content);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant SaveVectorAsync failed ({StatusCode}): {Error}", response.StatusCode, err);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant SaveVectorAsync exception for contentId {ContentId}", contentId);
            }
        }

        public async Task SaveCourseVectorAsync(int courseId, List<float> vector, string courseData)
        {
            try
            {
                await EnsureCollectionExistsAsync(CourseCollectionName);

                var uploadPayload = new
                {
                    points = new[]
                    {
                        new
                        {
                            id = (long)courseId,
                            vector = vector,
                            payload = new
                            {
                                courseId = courseId,
                                searchData = courseData
                            }
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(uploadPayload), Encoding.UTF8, "application/json");
                using var request = CreateRequest(HttpMethod.Put, $"collections/{CourseCollectionName}/points?wait=true", content);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant SaveCourseVectorAsync failed ({StatusCode}): {Error}", response.StatusCode, err);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant SaveCourseVectorAsync exception for courseId {CourseId}", courseId);
            }
        }

        public async Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> queryVector, int lessonId, int limit = 3, List<float>? vectorData = null)
        {
            // Delegate to the core search using the queryVector
            return await SearchSimilarTextsAsync(collectionName, queryVector, limit);
        }

        public async Task<List<string>> SearchSimilarTextsAsync(string collectionName, List<float> vectorData, int limit)
        {
            try
            {
                string url = $"collections/{collectionName}/points/search";

                var requestBody = new
                {
                    vector = vectorData,
                    limit = limit,
                    with_payload = true
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var request = CreateRequest(HttpMethod.Post, url, content);
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant text search returned {StatusCode}: {Error}", response.StatusCode, err);
                    return new List<string>();
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var results = new List<string>();

                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resultArray) && resultArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var point in resultArray.EnumerateArray())
                        {
                            if (point.TryGetProperty("payload", out var payload) &&
                                payload.TryGetProperty("text", out var textProp))
                            {
                                string? txt = textProp.GetString();
                                if (!string.IsNullOrEmpty(txt))
                                {
                                    results.Add(txt);
                                }
                            }
                        }
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant text search failed.");
                return new List<string>();
            }
        }

        public async Task<List<int>> SearchSimilarCoursesAsync(List<float> vectorData, int limit = 5)
        {
            try
            {
                string url = $"collections/{CourseCollectionName}/points/search";

                var requestBody = new
                {
                    vector = vectorData,
                    limit = limit,
                    with_payload = true
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var request = CreateRequest(HttpMethod.Post, url, content);
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant course search returned {StatusCode}: {Error}", response.StatusCode, err);
                    return new List<int>();
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var matchingCourseIds = new List<int>();

                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resultArray) && resultArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var point in resultArray.EnumerateArray())
                        {
                            if (point.TryGetProperty("payload", out var payload) &&
                                payload.TryGetProperty("courseId", out var courseIdProp))
                            {
                                if (courseIdProp.TryGetInt32(out int cId))
                                {
                                    matchingCourseIds.Add(cId);
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

        public async Task<bool> DeleteVectorAsync(string pointId)
        {
            try
            {
                var deletePayload = new
                {
                    points = new[] { pointId }
                };

                var content = new StringContent(JsonSerializer.Serialize(deletePayload), Encoding.UTF8, "application/json");
                using var request = CreateRequest(HttpMethod.Post, "collections/lesson_contents/points/delete", content);
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant DeleteVectorAsync string ID failed ({StatusCode}): {Error}", response.StatusCode, err);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant string ID delete failed.");
                return false;
            }
        }

        public async Task<bool> DeleteVectorAsync(List<long> pointIds)
        {
            if (pointIds == null || pointIds.Count == 0) return true;

            try
            {
                var deletePayload = new
                {
                    points = pointIds
                };

                var content = new StringContent(JsonSerializer.Serialize(deletePayload), Encoding.UTF8, "application/json");
                using var request = CreateRequest(HttpMethod.Post, "collections/lesson_contents/points/delete", content);
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Qdrant DeleteVectorAsync numeric IDs failed ({StatusCode}): {Error}", response.StatusCode, err);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Qdrant numeric IDs delete failed.");
                return false;
            }
        }
    }
}