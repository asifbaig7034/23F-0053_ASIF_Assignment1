using System.Security.Claims;
using MarksApi.Configuration;
using MarksApi.Data;
using MarksApi.Dtos;
using MarksApi.Services.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarksApi.Services.Reports;

public sealed class ReportService(
    AppDbContext db,
    IStudentService studentService,
    IOptions<GradingOptions> gradingOptions) : IReportService
{
    public Task<CourseStatsResponseDto?> GetCourseStatsAsync(
        int courseId,
        CancellationToken cancellationToken = default)
    {
        var passMark = gradingOptions.Value.PassMark;

        return db.Courses
            .AsNoTracking()
            .Where(course => course.Id == courseId)
            .Select(course => new CourseStatsResponseDto(
                course.Id,
                course.Code,
                course.Enrollments.Count(),
                course.Enrollments.Count(e => e.Mark.HasValue),
                course.Enrollments.Average(e => (decimal?)e.Mark),
                course.Enrollments.Max(e => e.Mark),
                course.Enrollments.Min(e => e.Mark),
                course.Enrollments.Count(e =>
                    e.Mark.HasValue && e.Mark.Value >= passMark)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<TranscriptLookupResult> GetTranscriptAsync(
        int studentId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken = default)
    {
        var studentLookup = await studentService.GetByIdAsync(
            studentId, caller, cancellationToken);

        if (studentLookup.IsForbidden)
        {
            return new TranscriptLookupResult(
                null, IsForbidden: true);
        }

        if (studentLookup.Student is null)
        {
            return new TranscriptLookupResult(
                null, IsForbidden: false);
        }

        var student = studentLookup.Student;
        var passMark = gradingOptions.Value.PassMark;

        var courses = await db.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId)
            .OrderBy(enrollment => enrollment.Course.Code)
            .ThenBy(enrollment => enrollment.CourseId)
            .Select(enrollment => new TranscriptCourseDto(
                enrollment.Course.Code,
                enrollment.Course.CreditHours,
                enrollment.Mark,
                enrollment.Mark.HasValue &&
                    enrollment.Mark.Value >= passMark))
            .ToListAsync(cancellationToken);

        var gradedCourses = courses
            .Where(course => course.Mark.HasValue)
            .ToArray();

        var totalCreditHours = gradedCourses
            .Sum(course => (decimal)course.CreditHours);

        var weightedTotal = gradedCourses
            .Sum(course =>
                (decimal)course.Mark.GetValueOrDefault() *
                course.CreditHours);

        decimal? weightedAverage = totalCreditHours == 0
            ? null
            : weightedTotal / totalCreditHours;

        var transcript = new StudentTranscriptResponseDto(
            student.Id,
            student.RegistrationNo,
            student.FullName,
            courses,
            weightedAverage);

        return new TranscriptLookupResult(
            transcript, IsForbidden: false);
    }
}
