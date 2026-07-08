using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;

namespace MiniLms.Repositories
{
    public class LessonRepository : GenericRepository<Lesson>, ILessonRepository
    {
        private readonly ApplicationDbContext _context;

        public LessonRepository(ApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        public Task DeleteAsync(Lesson lesson)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<Lesson>> GetByCourseIdAsync(int courseId)
        {
            return await _context.Lessons
                .AsNoTracking()
                .Where(l => l.CourseId == courseId)
                .OrderBy(l => l.WeekNumber)
                .ToListAsync();
        }
    }
}