using MiniLms.Models;
using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IEnrollmentRepository : IGenericRepository<Enrollment>
    {
        // Bir öğrencinin belirli bir derse kaydı var mı
        Task<Enrollment?> GetByStudentAndCourseIdAsync(int studentId, int courseId);
        Task<IEnumerable<Enrollment>> GetByStudentIdAsync(int studentId);
    }
}
