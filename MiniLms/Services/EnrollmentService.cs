using MiniLms.Interfaces;
using MiniLms.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private readonly IEnrollmentRepository _enrollmentRepository;

        public EnrollmentService(IEnrollmentRepository enrollmentRepository)
        {
            _enrollmentRepository = enrollmentRepository;
        }

        public async Task<IEnumerable<Enrollment>> GetAllEnrollmentsAsync()
        {
            return await _enrollmentRepository.GetAllAsync();
        }

        public async Task<Enrollment?> GetEnrollmentByIdAsync(int id)
        {
            return await _enrollmentRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<Enrollment>> GetEnrollmentsByStudentIdAsync(int studentId)
        {
            return await _enrollmentRepository.GetByStudentIdAsync(studentId);
        }

        // ÖĞRENCİYİ DERSE KAYDETME 
        public async Task EnrollStudentAsync(Enrollment enrollment)
        {
            if (enrollment == null)
                throw new ArgumentNullException(nameof(enrollment));

            // Bu öğrenci bu dersi zaten alıyor mu?
            var existingEnrollment = await _enrollmentRepository
                .GetByStudentAndCourseIdAsync(enrollment.StudentId, enrollment.CourseId);

            if (existingEnrollment != null)
            {
                throw new InvalidOperationException("Bu öğrenci bu derse zaten kayıtlı!");
            }

            // Eğer bir engel yoksa kaydı gerçekleştiriyoruz
            await _enrollmentRepository.AddAsync(enrollment);
        }

        public async Task RemoveEnrollmentAsync(int id)
        {
            var enrollment = await _enrollmentRepository.GetByIdAsync(id);
            if (enrollment == null)
            {
                throw new KeyNotFoundException("Silinmek istenen ders kaydı bulunamadı.");
            }

            await _enrollmentRepository.DeleteAsync(id);
        }
    }
}
