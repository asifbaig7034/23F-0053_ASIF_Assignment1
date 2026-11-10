using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MarksApi.Dtos;
using MarksApi.Services.Enrollments;

namespace MarksApi.Endpoints;

public static class EnrollmentEndpoints
{
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var courseEnrollments = app
            .MapGroup("/api/courses/{courseId:int}/enrollments")
            .RequireAuthorization("TeacherOnly")
            .WithTags("Enrollments and Marks");

        courseEnrollments.MapPost("", CreateAsync)
            .WithName("CreateEnrollment")
            .Produces<EnrollmentResponseDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        courseEnrollments.MapGet("", GetListAsync)
            .WithName("ListCourseEnrollments")
            .Produces<PagedResultDto<EnrollmentListItemDto>>(
                StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var enrollments = app.MapGroup("/api/enrollments")
            .RequireAuthorization("TeacherOnly")
            .WithTags("Enrollments and Marks");

        enrollments.MapGet("/{id:int}", GetByIdAsync)
            .WithName("GetEnrollmentById")
            .Produces<EnrollmentResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        enrollments.MapPut("/{id:int}/mark", UpdateMarkAsync)
            .WithName("UpdateEnrollmentMark")
            .Produces<EnrollmentResponseDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        enrollments.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteEnrollment")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        int courseId,
        EnrollmentCreateRequestDto request,
        IEnrollmentService enrollmentService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors(request);

        if (errors.Count > 0)
        {
            return InvalidRequest(errors, context);
        }

        var enrollment = await enrollmentService.CreateAsync(
            courseId, request.StudentId, cancellationToken);

        return Results.Created(
            $"/api/enrollments/{enrollment.Id}", enrollment);
    }

    private static async Task<IResult> GetListAsync(
        int courseId,
        IEnrollmentService enrollmentService,
        HttpContext context,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 10,
        string? search = null,
        string? sort = null)
    {
        var errors = new Dictionary<string, string[]>();

        if (page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (pageSize < 1 || pageSize > 50)
        {
            errors["pageSize"] =
                ["Page size must be between 1 and 50."];
        }

        if (page > 0 && pageSize >= 1 && pageSize <= 50 &&
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            errors["page"] = ["The requested page is too large."];
        }

        if (search is { Length: > 150 })
        {
            errors["search"] =
                ["Search must not exceed 150 characters."];
        }

        var sortValue = string.IsNullOrWhiteSpace(sort)
            ? "id"
            : sort.Trim();

        if (sortValue is not (
            "id" or "-id" or
            "studentId" or "-studentId" or
            "fullName" or "-fullName" or
            "mark" or "-mark" or
            "enrolledAtUtc" or "-enrolledAtUtc"))
        {
            errors["sort"] =
            [
                "Use id, studentId, fullName, mark or enrolledAtUtc. " +
                "Prefix with - for descending order."
            ];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(
                errors,
                detail: "Correct the query parameters and try again.",
                instance: context.Request.Path.Value);
        }

        var result = await enrollmentService.ListAsync(
            courseId,
            page,
            pageSize,
            search,
            sortValue,
            cancellationToken);

        if (result is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Course not found",
                detail: "The requested course was not found.",
                instance: context.Request.Path.Value);
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetByIdAsync(
        int id,
        IEnrollmentService enrollmentService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var enrollment = await enrollmentService.GetByIdAsync(
            id, cancellationToken);

        if (enrollment is null)
        {
            return EnrollmentNotFound(context);
        }

        return Results.Ok(enrollment);
    }

    private static async Task<IResult> UpdateMarkAsync(
        int id,
        MarkUpdateRequestDto request,
        IEnrollmentService enrollmentService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors(request);

        if (errors.Count > 0)
        {
            return InvalidRequest(errors, context);
        }

        var enrollment = await enrollmentService.UpdateMarkAsync(
            id,
            request.Mark.GetValueOrDefault(),
            cancellationToken);

        if (enrollment is null)
        {
            return EnrollmentNotFound(context);
        }

        return Results.Ok(enrollment);
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        IEnrollmentService enrollmentService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var deleted = await enrollmentService.DeleteAsync(
            id, cancellationToken);

        if (!deleted)
        {
            return EnrollmentNotFound(context);
        }

        return Results.NoContent();
    }

    private static IResult EnrollmentNotFound(HttpContext context)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Enrollment not found",
            detail: "The requested enrollment was not found.",
            instance: context.Request.Path.Value);
    }

    private static IResult InvalidRequest(
        Dictionary<string, string[]> errors,
        HttpContext context)
    {
        return Results.ValidationProblem(
            errors,
            detail: "Correct the indicated fields and try again.",
            instance: context.Request.Path.Value);
    }

    private static Dictionary<string, string[]> GetValidationErrors(
        object request)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        return results
            .SelectMany(result => result.MemberNames
                .DefaultIfEmpty("request")
                .Select(member => new
                {
                    Key = JsonNamingPolicy.CamelCase.ConvertName(member),
                    Message = result.ErrorMessage ?? "Invalid value."
                }))
            .GroupBy(error => error.Key)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Message)
                    .Distinct()
                    .ToArray());
    }
}
