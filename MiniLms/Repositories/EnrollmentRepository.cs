using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Repositories
{
    public class EnrollmentRepository : IEnrollmentRepository
    {
        private readonly ApplicationDbContext _context;

        public EnrollmentRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Enrollment>> GetAllAsync()
        {
            // Kayıtları listelerken hem öğrenci hem ders bilgilerini JOIN (Include) yapıyoruz
            return await _context.Enrollments
                .Include(e => e.Student)
                .Include(e => e.Course)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Enrollment?> GetByIdAsync(int id)
        {
            // composite key (Bileşik anahtar) kullandığımız için FindAsync yerine 
            // FirstOrDefaultAsync ile ID bazlı çekmek tekil kayıtlarda daha güvenlidir.
            return await _context.Enrollments
                .Include(e => e.Student)
                .Include(e => e.Course)
                .FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task AddAsync(Enrollment entity)
        {
            await _context.Enrollments.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Enrollment entity)
        {
            _context.Enrollments.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var enrollment = await GetByIdAsync(id);
            if (enrollment != null)
            {
                _context.Enrollments.Remove(enrollment);
                await _context.SaveChangesAsync();
            }
        }

        // Öğrenci ve Ders ID'sine göre  kayıt kontrolü sorgusu
        public async Task<Enrollment?> GetByStudentAndCourseIdAsync(int studentId, int courseId)
        {
            return await _context.Enrollments
                .FirstOrDefaultAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        }

        public async Task<IEnumerable<Enrollment>> GetByStudentIdAsync(int studentId)
        {
            return await _context.Enrollments
                .Include(e => e.Course)
                .Where(e => e.StudentId == studentId)
                .AsNoTracking()
                .ToListAsync();
        }
    }
}
