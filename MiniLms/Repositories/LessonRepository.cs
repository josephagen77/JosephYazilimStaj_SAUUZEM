using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;

namespace MiniLms.Repositories
{
    public class LessonRepository : GenericRepository<Lesson>, ILessonRepository
    {
       
        public LessonRepository(ApplicationDbContext context) : base(context)
        {
           
        }

        public async Task DeleteAsync(Lesson lesson)
        {
            if (lesson == null) return;
            _context.Lessons.Remove(lesson);
            await _context.SaveChangesAsync();
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