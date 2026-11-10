using MarksApi.Dtos;
using MarksApi.Services.Reports;

namespace MarksApi.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/courses/{courseId:int}/stats",
                GetCourseStatsAsync)
            .RequireAuthorization("TeacherOnly")
            .WithName("GetCourseStats")
            .WithTags("Reports")
            .Produces<CourseStatsResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet(
                "/api/students/{id:int}/transcript",
                GetTranscriptAsync)
            .RequireAuthorization("TeacherOrStudent")
            .WithName("GetStudentTranscript")
            .WithTags("Reports")
            .Produces<StudentTranscriptResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetCourseStatsAsync(
        int courseId,
        IReportService reportService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var stats = await reportService.GetCourseStatsAsync(
            courseId, cancellationToken);

        if (stats is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Course not found",
                detail: "The requested course was not found.",
                instance: context.Request.Path.Value);
        }

        return Results.Ok(stats);
    }

    private static async Task<IResult> GetTranscriptAsync(
        int id,
        IReportService reportService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetTranscriptAsync(
            id, context.User, cancellationToken);

        if (result.IsForbidden)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "You may only read your own student transcript.",
                instance: context.Request.Path.Value);
        }

        if (result.Transcript is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Student not found",
                detail: "The requested student was not found.",
                instance: context.Request.Path.Value);
        }

        return Results.Ok(result.Transcript);
    }
}
