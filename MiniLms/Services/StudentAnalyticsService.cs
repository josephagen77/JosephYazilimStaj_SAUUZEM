using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;

namespace MiniLms.Services
{
    public class StudentAnalyticsService : IStudentAnalyticsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiService _aiService;

        public StudentAnalyticsService(ApplicationDbContext context, IAiService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        public async Task<string> GenerateStudentInsightAsync(string userId, int courseId)
        {
            // 1. Veritabanından öğrencinin bu kurstaki son 20 mesajını getir
            var chatHistory = await _context.ChatMessages
                .Where(m => m.UserId == userId && m.CourseId == courseId)
                .OrderByDescending(m => m.Timestamp) // Eğer modelinizde bu alanın adı 'CreatedAt' veya 'Date' ise burayı güncelleyin
                .Take(20)
                .ToListAsync();

            // Tarih sırasına koymak için bellekte tersine çeviriyoruz
            chatHistory = chatHistory.OrderBy(m => m.Timestamp).ToList();

            if (!chatHistory.Any())
            {
                return "Bu öğrenci henüz bu derste yapay zeka asistanı ile etkileşime girmemiştir. Analiz edilecek yeterli veri yok.";
            }

            // 2. Sohbet transkriptini oluştur
            var transcript = string.Join("\n", chatHistory.Select(m =>
                $"{(m.Role == "user" ? "Öğrenci" : "Yapay Zeka")}: {m.Content}"));

            // 3. Yapay Zeka için Psikolojik Analiz Promptu
            string prompt = $@"
                Sen uzman bir eğitim psikoloğu ve veri analistisin. 
                Aşağıda bir öğrencinin LMS (Öğrenim Yönetim Sistemi) üzerindeki ders asistanıyla yaptığı son sohbet geçmişi bulunuyor.
                Öğretmeni için bu öğrencinin durumunu analiz et ve SADECE markdown formatında yapılandırılmış bir rapor sun.
                
                Lütfen şu başlıkları kesinlikle içersin:
                ### 🔴 Bilgi Boşlukları
                (Öğrencinin zorlandığı, sürekli sorduğu veya yanlış anladığı teknik konular)
                
                ### 🧠 Öğrenme Eğilimi
                (Öğrencinin soru sorma tarzı nasıl? Örnek mi istiyor, özet mi? Hangi yöntemle daha iyi anlıyor?)
                
                ### 💡 Öğretmene Tavsiyeler
                (Bu öğrenciye derste veya ödevlerde nasıl daha iyi yardımcı olunabilir? 3 aksiyon edilebilir, net ipucu ver.)
                
                SOHBET GEÇMİŞİ:
                {transcript}
            ";

            try
            {
                // 🎯 DÜZELTİLDİ: Arayüzünüzdeki genel sohbet/metin metodu olan 'SummarizeTextAsync' kullanıldı.
                var response = await _aiService.SummarizeTextAsync(prompt, "gemini", null);
                return response;
            }
            catch (Exception ex)
            {
                return $"Yapay zeka analizi oluşturulurken bir hata meydana geldi: {ex.Message}";
            }
        }
    }
}