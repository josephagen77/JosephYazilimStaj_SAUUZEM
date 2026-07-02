using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace MiniLms.Services
{
    public class CourseDocumentService : ICourseDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CourseDocumentService(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task SaveDocumentAsync(int courseId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Geçersiz dosya!");

            // 1. Dosyaların yükleneceği fiziksel klasör yolunu belirle (wwwroot/uploads)
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");

            // Eğer klasör yoksa otomatik oluştur
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // 2. Sunucuda aynı isimde dosya çakışmasını önlemek için benzersiz bir isim üret (GUID)
            string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            // 3. Dosyayı fiziksel olarak sunucu diskine kaydet
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // 4. Veri tabanına kaydetmek için nesneyi hazırla
            var document = new CourseDocument
            {
                CourseId = courseId,
                FileName = file.FileName, // Kullanıcının gördüğü isim (Örn: Ödev1.pdf)
                FilePath = "/uploads/" + uniqueFileName, // Siteden erişilecek web adresi
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
            // DÜZELTİLEN KISIM: Başına 'await' eklenerek derleme hatası kesin olarak çözüldü
            return await _context.CourseDocuments.FindAsync(id);
        }

        public async Task DeleteDocumentAsync(int id)
        {
            var document = await _context.CourseDocuments.FindAsync(id);
            if (document != null)
            {
                // 1. Fiziksel dosyayı diskten sil
                string physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, document.FilePath.TrimStart('/'));
                if (File.Exists(physicalPath))
                {
                    File.Delete(physicalPath);
                }

                // 2. Veri tabanı kaydını sil
                _context.CourseDocuments.Remove(document);
                await _context.SaveChangesAsync();
            }
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
            if (extension != ".pdf" && extension != ".txt")
            {
                throw new InvalidOperationException("Sadece PDF veya TXT dosyası yükleyebilirsiniz.");
            }

            string uniqueFileName = Guid.NewGuid() + "_" + Path.GetFileName(file.FileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            string extractedText = extension == ".pdf"
                ? ExtractTextFromPdf(filePath)
                : await File.ReadAllTextAsync(filePath);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new InvalidOperationException("Dosyadan okunabilir metin çıkarılamadı.");
            }

            var document = new CourseDocument
            {
                CourseId = courseId,
                FileName = file.FileName,
                FilePath = "/uploads/" + uniqueFileName,
                UploadedDate = DateTime.Now
            };

            await _context.CourseDocuments.AddAsync(document);

            var lesson = await _context.Lessons
                .Where(l => l.CourseId == courseId && l.Title == "Yüklenen Dokümanlar")
                .FirstOrDefaultAsync();

            if (lesson == null)
            {
                int nextWeekNumber = await _context.Lessons
                    .Where(l => l.CourseId == courseId)
                    .Select(l => (int?)l.WeekNumber)
                    .MaxAsync() ?? 0;

                lesson = new Lesson
                {
                    CourseId = courseId,
                    Title = "Yüklenen Dokümanlar",
                    WeekNumber = nextWeekNumber + 1
                };

                await _context.Lessons.AddAsync(lesson);
                await _context.SaveChangesAsync();
            }

            int nextOrder = await _context.LessonContents
                .Where(c => c.LessonId == lesson.Id)
                .Select(c => (int?)c.Order)
                .MaxAsync() ?? 0;

            foreach (string chunk in SplitText(extractedText, 3000))
            {
                nextOrder++;

                await _context.LessonContents.AddAsync(new LessonContent
                {
                    LessonId = lesson.Id,
                    Title = $"{Path.GetFileNameWithoutExtension(file.FileName)} - Bölüm {nextOrder}",
                    Text = chunk,
                    Body = chunk,
                    ResourceUrl = document.FilePath,
                    Order = nextOrder,
                    Type = extension == ".pdf" ? "Pdf" : "Text",
                    IsIndexed = false
                });
            }

            await _context.SaveChangesAsync();
        }

        private static string ExtractTextFromPdf(string filePath)
        {
            using var document = PdfDocument.Open(filePath);
            return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
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
