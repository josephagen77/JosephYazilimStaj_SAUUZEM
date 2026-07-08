using MiniLms.Models;
using MiniLms.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Interfaces
{
    public interface IStudentService
    {
        Task<IEnumerable<Student>> GetAllStudentsAsync();
        Task<Student?> GetStudentByIdAsync(int id);
        Task<Student?> GetStudentWithEnrollmentsAsync(int id);
        Task<Student?> GetStudentByNumberAsync(string studentNumber);
        Task AddStudentAsync(StudentCreateViewModel model);
        Task UpdateStudentAsync(StudentEditViewModel model);
        Task DeleteStudentAsync(int id);
    }
}
