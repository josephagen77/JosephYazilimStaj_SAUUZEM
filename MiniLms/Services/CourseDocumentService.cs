using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // 🎯 YENİ: IServiceScopeFactory için eklendi
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using Coravel.Queuing.Interfaces;

namespace MiniLms.Services
{
    public class CourseDocumentService : ICourseDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IVectorDbService _vectorDbService;
        private readonly IQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory; // 🎯 YENİ: Arka plan thread'leri için DB factory

        public CourseDocumentService(
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IVectorDbService vectorDbService,
            IQueue queue,
            IServiceScopeFactory scopeFactory) // 🎯 YENİ: Dependency Injection güncellendi
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _vectorDbService = vectorDbService;
            _queue = queue;
            _scopeFactory = scopeFactory;
        }

        public async Task SaveDocumentAsync(int courseId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Geçersiz dosya!");

            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");

            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var document = new CourseDocument
            {
                CourseId = courseId,
                FileName = file.FileName,
                FilePath = "/uploads/" + uniqueFileName,
                UploadedDate = DateTime.Now
            };

            await _context.CourseDocuments.AddAsync(document);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<CourseDocument>> GetDocumentsByCourseIdAsync(int courseId)
        {
            return await _context.CourseDocuments
                .Where(d => d.CourseId == courseId)
                .OrderByDescending(d => d.UploadedDate)
                .ToListAsync();
        }

        public async Task<CourseDocument?> GetDocumentByIdAsync(int id)
        {
            return await _context.CourseDocuments.FindAsync(id);
        }

        public async Task DeleteDocumentAsync(int id)
        {
            var document = await _context.CourseDocuments.FindAsync(id);
            if (document != null)
            {
                var associatedContents = await _context.LessonContents
                    .Where(content => content.ResourceUrl == document.FilePath)
                    .ToListAsync();

                if (associatedContents.Any())
                {
                    var associatedContentIds = associatedContents.Select(c => c.Id).ToList();
                    var associatedLessonIds = associatedContents.Select(c => c.LessonId).Distinct().ToList();
                    var pointIds = associatedContents.Select(c => (long)c.Id).ToList();

                    await _vectorDbService.DeleteVectorAsync(pointIds);

                    _context.LessonContents.RemoveRange(associatedContents);

                    foreach (int lessonId in associatedLessonIds)
                    {
                        bool hasOtherContents = await _context.LessonContents
                            .AnyAsync(content => content.LessonId == lessonId && !associatedContentIds.Contains(content.Id));

                        if (!hasOtherContents)
                        {
                            var emptyLesson = await _context.Lessons.FindAsync(lessonId);
                            if (emptyLesson != null &&
                                (emptyLesson.Title == "Yüklenen Dokümanlar" ||
                                 emptyLesson.Title.StartsWith("Doküman Konuları:", StringComparison.OrdinalIgnoreCase)))
                            {
                                _context.Lessons.Remove(emptyLesson);
                            }
                        }
                    }
                }

                string physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, document.FilePath.TrimStart('/'));
                if (File.Exists(physicalPath))
                {
                    File.Delete(physicalPath);
                }

                _context.CourseDocuments.Remove(document);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<string>> GetDocumentTextChunksAsync(int documentId, int maxChunks = 5)
        {
            var document = await _context.CourseDocuments.FindAsync(documentId);
            if (document == null)
            {
                return new List<string>();
            }

            var indexedChunks = await _context.LessonContents
                .Where(content => content.ResourceUrl == document.FilePath)
                .OrderBy(content => content.Order)
                .Select(content => !string.IsNullOrWhiteSpace(content.Body) ? content.Body : content.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(maxChunks)
                .ToListAsync();

            if (indexedChunks.Count > 0)
            {
                return indexedChunks;
            }

            string physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, document.FilePath.TrimStart('/'));
            if (!File.Exists(physicalPath))
            {
                return new List<string>();
            }

            string extension = Path.GetExtension(physicalPath).ToLowerInvariant();
            string extractedText = await ExtractTextFromFileAsync(physicalPath, extension);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return new List<string>();
            }

            return SplitText(extractedText, 3000)
                .Take(maxChunks)
                .ToList();
        }

        public async Task EnsureDocumentTopicLessonsAsync(int courseId)
        {
            var documents = await _context.CourseDocuments
                .Where(document => document.CourseId == courseId)
                .OrderBy(document => document.UploadedDate)
                .ToListAsync();

            foreach (var document in documents)
            {
                bool topicsAlreadyCreated = await _context.LessonContents
                    .AnyAsync(content => content.ResourceUrl == document.FilePath && content.Type == "DocumentTopic");

                if (topicsAlreadyCreated)
                {
                    continue;
                }

                string extractedText = await ReadDocumentTextAsync(document);
                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    // _context kullanıyoruz çünkü bu metot zaten ana HTTP thread'inde çağrılıyor
                    await AddDocumentTopicLessonAsync(_context, courseId, document, extractedText);
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task UploadDocumentAsync(int courseId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Geçersiz dosya!");

            var courseExists = await _context.Courses.AnyAsync(c => c.Id == courseId);
            if (!courseExists)
                throw new KeyNotFoundException("Doküman eklenecek kurs bulunamadı.");

            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            var allowedExtensions = new HashSet<string> { ".pdf", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar" };
            if (!allowedExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Desteklenmeyen dosya formatı. Lütfen akademik bir belge yükleyin.");
            }

            string uniqueFileName = Guid.NewGuid() + "_" + Path.GetFileName(file.FileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var document = new CourseDocument
            {
                CourseId = courseId,
                FileName = file.FileName,
                FilePath = "/uploads/" + uniqueFileName,
                UploadedDate = DateTime.Now
            };

            await _context.CourseDocuments.AddAsync(document);
            await _context.SaveChangesAsync();

            // 🎯 GÜNCELLENDİ: Arka Plan Kuyruğu - Özel veritabanı bağlantısı ile
            _queue.QueueTask(async () =>
            {
                try
                {
                    // Arka planda çalışan task için güvenli bir bağımsız bağlantı oluştur
                    using var scope = _scopeFactory.CreateScope();
                    var scopedDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    string extractedText = await ExtractTextFromFileAsync(filePath, extension);

                    if (!string.IsNullOrWhiteSpace(extractedText))
                    {
                        await ProcessDocumentBackgroundAsync(scopedDbContext, courseId, document, extractedText, extension, file.FileName);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Arka Plan Hatası] Doküman işlenemedi: {ex.Message}");
                }
            });
        }

        // 🎯 GÜNCELLENDİ: Parametre olarak scopedDbContext alır
        private async Task ProcessDocumentBackgroundAsync(ApplicationDbContext scopedDbContext, int courseId, CourseDocument document, string extractedText, string extension, string fileName)
        {
            await AddDocumentTopicLessonAsync(scopedDbContext, courseId, document, extractedText);

            var lesson = await scopedDbContext.Lessons
                .Where(l => l.CourseId == courseId && l.Title == "Yüklenen Dokümanlar")
                .FirstOrDefaultAsync();

            if (lesson == null)
            {
                int nextWeekNumber = await scopedDbContext.Lessons
                    .Where(l => l.CourseId == courseId)
                    .Select(l => (int?)l.WeekNumber)
                    .MaxAsync() ?? 0;

                lesson = new Lesson
                {
                    CourseId = courseId,
                    Title = "Yüklenen Dokümanlar",
                    WeekNumber = nextWeekNumber + 1
                };

                await scopedDbContext.Lessons.AddAsync(lesson);
                await scopedDbContext.SaveChangesAsync();
            }

            int nextOrder = await scopedDbContext.LessonContents
                .Where(c => c.LessonId == lesson.Id)
                .Select(c => (int?)c.Order)
                .MaxAsync() ?? 0;

            foreach (string chunk in SplitText(extractedText, 3000))
            {
                nextOrder++;

                await scopedDbContext.LessonContents.AddAsync(new LessonContent
                {
                    LessonId = lesson.Id,
                    Title = $"{Path.GetFileNameWithoutExtension(fileName)} - Bölüm {nextOrder}",
                    Text = chunk,
                    Body = chunk,
                    ResourceUrl = document.FilePath,
                    Order = nextOrder,
                    Type = extension == ".pdf" ? "Pdf" : "Text",
                    IsIndexed = false
                });
            }

            await scopedDbContext.SaveChangesAsync();
        }

        // 🎯 GÜNCELLENDİ: Parametre olarak dbContext alır
        private async Task AddDocumentTopicLessonAsync(ApplicationDbContext dbContext, int courseId, CourseDocument document, string extractedText)
        {
            var topicHeadings = ExtractTopicHeadings(extractedText, document.FileName);
            if (topicHeadings.Count == 0) return;

            int nextWeekNumber = await dbContext.Lessons
                .Where(l => l.CourseId == courseId && l.Title != "Yüklenen Dokümanlar")
                .Select(l => (int?)l.WeekNumber)
                .MaxAsync() ?? 0;

            var topicLesson = new Lesson
            {
                CourseId = courseId,
                Title = $"Doküman Konuları: {Path.GetFileNameWithoutExtension(document.FileName)}",
                WeekNumber = nextWeekNumber + 1
            };

            await dbContext.Lessons.AddAsync(topicLesson);
            await dbContext.SaveChangesAsync();

            int order = 0;
            foreach (string heading in topicHeadings)
            {
                order++;
                await dbContext.LessonContents.AddAsync(new LessonContent
                {
                    LessonId = topicLesson.Id,
                    Title = heading,
                    Text = $"Kaynak dokümandan çıkarılan konu başlığı: {heading}",
                    Body = heading,
                    ResourceUrl = document.FilePath,
                    Order = order,
                    Type = "DocumentTopic",
                    IsIndexed = true
                });
            }
        }

        private async Task<string> ExtractTextFromFileAsync(string filePath, string extension)
        {
            return extension switch
            {
                ".pdf" => ExtractTextFromPdf(filePath),
                ".txt" => await File.ReadAllTextAsync(filePath),
                ".docx" => ExtractTextFromDocx(filePath),
                ".pptx" => ExtractTextFromPptx(filePath),
                _ => string.Empty
            };
        }

        private static string ExtractTextFromPdf(string filePath)
        {
            try
            {
                using var document = PdfDocument.Open(filePath);
                return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
            }
            catch { return string.Empty; }
        }

        private static string ExtractTextFromDocx(string filePath)
        {
            try
            {
                using var wordDoc = WordprocessingDocument.Open(filePath, false);
                return wordDoc.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string ExtractTextFromPptx(string filePath)
        {
            try
            {
                using var pptDoc = PresentationDocument.Open(filePath, false);
                var texts = new List<string>();

                if (pptDoc.PresentationPart?.SlideParts != null)
                {
                    foreach (var slidePart in pptDoc.PresentationPart.SlideParts)
                    {
                        if (slidePart.Slide != null)
                        {
                            var slideTexts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(t => t.Text);
                            texts.AddRange(slideTexts);
                        }
                    }
                }
                return string.Join(" ", texts);
            }
            catch { return string.Empty; }
        }

        private async Task<string> ReadDocumentTextAsync(CourseDocument document)
        {
            string physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, document.FilePath.TrimStart('/'));
            if (!File.Exists(physicalPath))
            {
                return string.Empty;
            }

            string extension = Path.GetExtension(physicalPath).ToLowerInvariant();
            return await ExtractTextFromFileAsync(physicalPath, extension);
        }

        private static List<string> ExtractTopicHeadings(string text, string fileName)
        {
            var topics = new List<string>();

            string normalizedText = Regex.Replace(text ?? string.Empty, @"[ \t]+", " ");
            var lines = normalizedText
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => CleanHeading(line))
                .Where(line => IsLikelyHeading(line))
                .ToList();

            foreach (string line in lines)
            {
                AddTopic(topics, line);
                if (topics.Count >= 18) return topics;
            }

            if (topics.Count < 4)
            {
                foreach (Match match in Regex.Matches(
                    normalizedText,
                    @"(?:^|[.!?]\s+)((?:\d+(?:\.\d+)*\.?\s+|Hafta\s+\d+[:\-.]?\s+|Bölüm\s+\d+[:\-.]?\s+|Chapter\s+\d+[:\-.]?\s+|Lecture\s+\d+[:\-.]?\s+)[A-ZÇĞİÖŞÜa-zçğıöşü][^.!?\r\n]{4,90})",
                    RegexOptions.IgnoreCase))
                {
                    AddTopic(topics, CleanHeading(match.Groups[1].Value));
                    if (topics.Count >= 18) return topics;
                }
            }

            if (topics.Count == 0)
            {
                AddTopic(topics, Path.GetFileNameWithoutExtension(fileName));
            }

            return topics;
        }

        private static string CleanHeading(string heading)
        {
            heading = Regex.Replace(heading ?? string.Empty, @"\s+", " ").Trim();
            heading = Regex.Replace(heading, @"^[•\-–—*]+\s*", string.Empty).Trim();
            heading = Regex.Replace(heading, @"\s+\.{2,}\s*\d+$", string.Empty).Trim();
            heading = heading.Trim(':', '-', '–', '—', '.', ' ');

            return heading.Length > 110 ? heading.Substring(0, 110).Trim() : heading;
        }

        private static bool IsLikelyHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 4 || line.Length > 110) return false;
            if (line.Count(char.IsLetter) < 3 || line.EndsWith(",", StringComparison.Ordinal)) return false;
            if (Regex.IsMatch(line, @"^(table|figure|şekil|tablo|page|sayfa|references|kaynakça)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(line, @"^(\d+(\.\d+)*\.?\s+|Hafta\s+\d+[:\-.]?\s+|Bölüm\s+\d+[:\-.]?\s+|Konu\s+\d+[:\-.]?\s+|Chapter\s+\d+[:\-.]?\s+|Lecture\s+\d+[:\-.]?\s+)", RegexOptions.IgnoreCase)) return true;

            int letterCount = line.Count(char.IsLetter);
            int upperCount = line.Count(char.IsUpper);
            bool mostlyUpper = letterCount > 0 && upperCount >= Math.Max(3, (int)(letterCount * 0.65));

            if (mostlyUpper && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 10) return true;

            bool shortTitle = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8 &&
                              !line.Contains(". ") &&
                              !line.EndsWith(".", StringComparison.Ordinal);

            return shortTitle && char.IsUpper(line[0]);
        }

        private static void AddTopic(List<string> topics, string topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return;

            bool exists = topics.Any(existing =>
                existing.Equals(topic, StringComparison.OrdinalIgnoreCase) ||
                existing.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                topic.Contains(existing, StringComparison.OrdinalIgnoreCase));

            if (!exists) topics.Add(topic);
        }

        private static IEnumerable<string> SplitText(string text, int chunkSize)
        {
            for (int start = 0; start < text.Length; start += chunkSize)
            {
                int length = Math.Min(chunkSize, text.Length - start);
                string chunk = text.Substring(start, length).Trim();

                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }
}