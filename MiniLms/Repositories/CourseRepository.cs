using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;

namespace MiniLms.Repositories
{
    public class CourseRepository
        : GenericRepository<Course>, ICourseRepository
    {
       

        public CourseRepository(ApplicationDbContext context)
            : base(context)
        {
            
        }

        public async Task<Course?> GetByCourseCodeAsync(string courseCode)
        {
            return await _context.Courses
                .FirstOrDefaultAsync(x => x.CourseCode == courseCode);
        }
    }
}