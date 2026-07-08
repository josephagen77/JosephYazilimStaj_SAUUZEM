using MiniLms.Models;
using MiniLms.ViewModels;

namespace MiniLms.Interfaces
{
    public interface ILessonService
    {
        Task<IEnumerable<LessonViewModel>> GetAllAsync();

        Task<IEnumerable<LessonViewModel>> GetByCourseIdAsync(int courseId);

        Task<LessonViewModel?> GetByIdAsync(int id);

        Task  AddAsync(LessonViewModel model);

        Task UpdateAsync(LessonViewModel model);

        Task DeleteAsync(int id);
        Task UpdateAsync(Lesson lesson);
        Task AddAsync(Lesson lesson);
    }
}