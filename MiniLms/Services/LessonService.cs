using AutoMapper;
using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.ViewModels;

namespace MiniLms.Services
{
    public class LessonService : ILessonService
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IMapper _mapper;

        public LessonService(ILessonRepository lessonRepository, IMapper mapper)
        {
            _lessonRepository = lessonRepository;
            _mapper = mapper;
        }

        public async Task<IEnumerable<LessonViewModel>> GetAllAsync()
        {
            var lessons = await _lessonRepository.GetAllAsync();

            return _mapper.Map<IEnumerable<LessonViewModel>>(lessons);
        }

        public async Task<IEnumerable<LessonViewModel>> GetByCourseIdAsync(int courseId)
        {
            var lessons = await _lessonRepository.GetByCourseIdAsync(courseId);

            return _mapper.Map<IEnumerable<LessonViewModel>>(lessons);
        }

        public async Task<LessonViewModel?> GetByIdAsync(int id)
        {
            var lesson = await _lessonRepository.GetByIdAsync(id);

            if (lesson == null)
                return null;

            return _mapper.Map<LessonViewModel>(lesson);
        }

        public async Task AddAsync(LessonViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var lesson = _mapper.Map<Lesson>(model);

            await _lessonRepository.AddAsync(lesson);
        }

        public async Task UpdateAsync(LessonViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var lesson = _mapper.Map<Lesson>(model);

            await _lessonRepository.UpdateAsync(lesson);
        }

        public async Task DeleteAsync(int id)
        {
            await _lessonRepository.DeleteAsync(id);
        }

        public Task UpdateAsync(Lesson lesson)
        {
            throw new NotImplementedException();
        }

        public Task AddAsync(Lesson lesson)
        {
            throw new NotImplementedException();
        }
    }
}