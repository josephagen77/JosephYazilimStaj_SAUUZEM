using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniLms.Interfaces;
using MiniLms.Models.Enums;
using System;
using System.Threading.Tasks;

namespace MiniLms.Controllers
{
    [Authorize(Policy = UserPolicies.TeacherOnly)]
    public class AnalyticsController : Controller
    {
        private readonly IStudentAnalyticsService _analyticsService;

        public AnalyticsController(IStudentAnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        // GET: /Analytics/StudentInsight?userId=B266200571&courseId=5
        public async Task<IActionResult> StudentInsight(string userId, int courseId)
        {
            try
            {
                // AI'dan Markdown formatında raporu al
                var reportMarkdown = await _analyticsService.GenerateStudentInsightAsync(userId, courseId);

                // View'a gönder
                ViewBag.ReportMarkdown = reportMarkdown;
                ViewBag.CourseId = courseId;
                ViewBag.UserId = userId;

                return View();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Analiz oluşturulurken hata oluştu: {ex.Message}";
                return RedirectToAction("Index", "Course");
            }
        }
    }
}