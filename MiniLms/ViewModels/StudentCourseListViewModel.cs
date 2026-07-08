using MiniLms.Models;

namespace MiniLms.ViewModels
{
    public class StudentCourseListViewModel
    {
        public IEnumerable<Course> Courses { get; set; } = Enumerable.Empty<Course>();
        public IEnumerable<Enrollment> Enrollments { get; set; } = Enumerable.Empty<Enrollment>();
        public HashSet<int> EnrolledCourseIds { get; set; } = new();
    }
}
