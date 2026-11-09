using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MarksApi.Dtos;
using MarksApi.Services.Courses;

namespace MarksApi.Endpoints;

public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/courses")
            .WithTags("Courses");

        group.MapGet("", GetListAsync)
            .RequireAuthorization("TeacherOrStudent")
            .WithName("ListCourses")
            .Produces<PagedResultDto<CourseResponseDto>>(
                StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:int}", GetByIdAsync)
            .RequireAuthorization("TeacherOrStudent")
            .WithName("GetCourseById")
            .Produces<CourseResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .RequireAuthorization("TeacherOnly")
            .WithName("CreateCourse")
            .Produces<CourseResponseDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", UpdateAsync)
            .RequireAuthorization("TeacherOnly")
            .WithName("UpdateCourse")
            .Produces<CourseResponseDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}", DeleteAsync)
            .RequireAuthorization("TeacherOnly")
            .WithName("DeleteCourse")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetListAsync(
        ICourseService courseService,
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
            "code" or "-code" or
            "title" or "-title" or
            "creditHours" or "-creditHours"))
        {
            errors["sort"] =
            [
                "Use id, code, title or creditHours. " +
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

        var result = await courseService.ListAsync(
            page,
            pageSize,
            search,
            sortValue,
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetByIdAsync(
        int id,
        ICourseService courseService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var course = await courseService.GetByIdAsync(
            id, cancellationToken);

        if (course is null)
        {
            return CourseNotFound(context);
        }

        return Results.Ok(course);
    }

    private static async Task<IResult> CreateAsync(
        CourseWriteRequestDto request,
        ICourseService courseService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors(request);

        if (errors.Count > 0)
        {
            return InvalidCourse(errors, context);
        }

        var course = await courseService.CreateAsync(
            request, cancellationToken);

        return Results.Created(
            $"/api/courses/{course.Id}", course);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        CourseWriteRequestDto request,
        ICourseService courseService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors(request);

        if (errors.Count > 0)
        {
            return InvalidCourse(errors, context);
        }

        var course = await courseService.UpdateAsync(
            id, request, cancellationToken);

        if (course is null)
        {
            return CourseNotFound(context);
        }

        return Results.Ok(course);
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ICourseService courseService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var deleted = await courseService.DeleteAsync(
            id, cancellationToken);

        if (!deleted)
        {
            return CourseNotFound(context);
        }

        return Results.NoContent();
    }

    private static IResult CourseNotFound(HttpContext context)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Course not found",
            detail: "The requested course was not found.",
            instance: context.Request.Path.Value);
    }

    private static IResult InvalidCourse(
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
