using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniLms.Interfaces;
using MiniLms.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MiniLms.Services
{
    public class VectorSyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IVectorDbService _vectorDbService;
        private readonly IAiService _aiService;
        private readonly ILogger<VectorSyncService> _logger;

        public VectorSyncService(
            IServiceProvider serviceProvider,
            IVectorDbService vectorDbService,
            IAiService aiService,
            ILogger<VectorSyncService> logger)
        {
            _serviceProvider = serviceProvider;
            _vectorDbService = vectorDbService;
            _aiService = aiService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _vectorDbService.EnsureCollectionExistsAsync("lesson_contents");
                await _vectorDbService.EnsureCollectionExistsAsync("course_vectors");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial Qdrant collection check failed in VectorSyncService.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                bool hadFailures = false;

                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<ILessonContentRepository>();
                        var context = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();

                        // 1. DERS DOKÜMAN VE İÇERİKLERİNİ QDRANT'A SENKRONİZE ET
                        var unIndexedContents = (await repo.GetUnIndexedAsync()).ToList();

                        foreach (LessonContent content in unIndexedContents)
                        {
                            string textToEmbed = !string.IsNullOrEmpty(content.Body) ? content.Body : content.Text;

                            if (string.IsNullOrWhiteSpace(textToEmbed))
                            {
                                await repo.MarkAsIndexedAsync(content.Id);
                                continue;
                            }

                            // Gemini'dan vektör koordinatlarını al
                            var vector = await _aiService.GetEmbeddingAsync(textToEmbed);

                            if (vector != null && vector.Count > 0)
                            {
                                // Qdrant Vector DB'ye kaydet
                                await _vectorDbService.SaveVectorAsync(
                                    collectionName: "lesson_contents",
                                    contentId: content.Id,
                                    lessonId: content.LessonId,
                                    vector: vector,
                                    originalText: textToEmbed
                                );

                                // Veritabanında indekslendi olarak işaretle
                                await repo.MarkAsIndexedAsync(content.Id);
                            }
                            else
                            {
                                hadFailures = true;
                            }
                        }

                        // 2. KURSLARI SEMANTİK ARAMA İÇİN QDRANT'A SENKRONİZE ET
                        var courses = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(context.Courses);
                        foreach (var course in courses)
                        {
                            string courseSearchText = $"{course.Title} - {course.Description}";
                            var courseVector = await _aiService.GetEmbeddingAsync(courseSearchText);
                            if (courseVector != null && courseVector.Count > 0)
                            {
                                await _vectorDbService.SaveCourseVectorAsync(course.Id, courseVector, courseSearchText);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in VectorSyncService execution cycle.");
                }

                // Başarısızlık varsa API'yi yormamak için 5 dk, normalde 2 dk bekle
                int delaySeconds = hadFailures ? 300 : 120;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
        }
    }
}