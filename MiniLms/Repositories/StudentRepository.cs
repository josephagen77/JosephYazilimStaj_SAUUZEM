using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;

namespace MiniLms.Repositories
{
    public class StudentRepository : IStudentRepository
    {
        private readonly ApplicationDbContext _context;

        public StudentRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Student>> GetAllAsync()
        {
            // Performans için AsNoTracking kullandık, sadece listeleme yapıyoruz.
            return await _context.Students
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Student?> GetByIdAsync(int id)
        {
            return await _context.Students.FindAsync(id);
        }

        public async Task AddAsync(Student entity)
        {
            await _context.Students.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Student entity)
        {
            _context.Students.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var student = await GetByIdAsync(id);
            if (student != null)
            {
                _context.Students.Remove(student);
                await _context.SaveChangesAsync();
            }
        }

        // YENİ EKLEDİĞİMİZ METOTLAR:

        // Öğrenciyi, aldığı dersler (Enrollments) ve ders detayları (Course) ile birlikte getirir (Eager Loading)
        public async Task<Student?> GetStudentWithEnrollmentsAsync(int id)
        {
            return await _context.Students
                .Include(s => s.Enrollments)
                    .ThenInclude(e => e.Course)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        // Öğrenci numarasına göre benzersiz arama yapar
        public async Task<Student?> GetByStudentNumberAsync(string studentNumber)
        {
            return await _context.Students
                .FirstOrDefaultAsync(s => s.StudentNumber == studentNumber);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}