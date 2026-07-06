using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using Microsoft.AspNetCore.Authorization;
using MiniLms.Models;

namespace MiniLms.Controllers
{
    [Authorize(Roles = "Teacher")]
    public class LessonController : Controller
    {
        private readonly ILessonService _lessonService;
        private readonly ILessonContentService _lessonContentService;
        private readonly ICourseService _courseService;

        public LessonController(
            ILessonService lessonService,
            ILessonContentService lessonContentService,
            ICourseService courseService)
        {
            _lessonService = lessonService;
            _lessonContentService = lessonContentService;
            _courseService = courseService;
        }

        
        public async Task<IActionResult> Details(int id)
        {
            var lesson = await _lessonService.GetByIdAsync(id);

            if (lesson == null)
                return NotFound();

            var contents = await _lessonContentService.GetByLessonIdAsync(id);

            ViewBag.Contents = contents;

            return View(lesson);
        }

        
        public IActionResult Create(int courseId)
        {
            ViewBag.CourseId = courseId;
            return View();
        }

        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Lesson lesson)
        {
            if (!ModelState.IsValid)
                return View(lesson);

            await _lessonService.AddAsync(lesson);

            return RedirectToAction("Details", "Course", new { id = lesson.CourseId });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var lesson = await _lessonService.GetByIdAsync(id);

            if (lesson == null)
                return NotFound();

            return View(lesson);
        }

        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Lesson lesson)
        {
            if (!ModelState.IsValid)
                return View(lesson);

            await _lessonService.UpdateAsync(lesson);

            return RedirectToAction("Details", "Course", new { id = lesson.CourseId });
        }

        
        public async Task<IActionResult> Delete(int id)
        {
            var lesson = await _lessonService.GetByIdAsync(id);

            if (lesson == null)
                return NotFound();

            return View(lesson);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var lesson = await _lessonService.GetByIdAsync(id);

            if (lesson == null)
                return NotFound();

            await _lessonService.DeleteAsync(id);

            return RedirectToAction("Details", "Course", new { id = lesson.CourseId });
        }
    }
}
