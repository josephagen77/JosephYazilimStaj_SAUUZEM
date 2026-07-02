using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;

namespace MiniLms.Repositories
{
    public class LessonContentRepository
        : GenericRepository<LessonContent>, ILessonContentRepository
    {
        private readonly ApplicationDbContext _context;

        public LessonContentRepository(ApplicationDbContext context)
            : base(context)
        {
            _context = context;
        }

        // Bir dersin tüm içeriklerini getir
        public async Task<IEnumerable<LessonContent>> GetByLessonIdAsync(int lessonId)
        {
            return await _context.LessonContents
                .Where(x => x.LessonId == lessonId)
                .OrderBy(x => x.Order)
                .ToListAsync();
        }

        // Aynı ders + aynı sıra var mı kontrolü
        public async Task<LessonContent?> GetByLessonAndOrderAsync(int lessonId, int order)
        {
            return await _context.LessonContents
                .FirstOrDefaultAsync(x =>
                    x.LessonId == lessonId &&
                    x.Order == order);
        }

        // AI için ileride kullanacağız (çok önemli)
        public async Task<IEnumerable<LessonContent>> GetUnIndexedAsync()
        {
            return await _context.LessonContents
                .Where(x => !x.IsIndexed)
                .ToListAsync();
        }

        // AI sync sonrası kullanılacak
        public async Task MarkAsIndexedAsync(int id)
        {
            var entity = await _context.LessonContents.FindAsync(id);

            if (entity == null)
                return;

            entity.IsIndexed = true;

            _context.LessonContents.Update(entity);
            await _context.SaveChangesAsync();
        }

    }
}
