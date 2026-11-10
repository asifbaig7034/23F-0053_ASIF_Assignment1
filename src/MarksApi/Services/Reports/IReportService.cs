using System.Security.Claims;
using MarksApi.Dtos;

namespace MarksApi.Services.Reports;

public interface IReportService
{
    Task<CourseStatsResponseDto?> GetCourseStatsAsync(
        int courseId,
        CancellationToken cancellationToken = default);

    Task<TranscriptLookupResult> GetTranscriptAsync(
        int studentId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken = default);
}

public sealed record TranscriptLookupResult(
    StudentTranscriptResponseDto? Transcript,
    bool IsForbidden);
