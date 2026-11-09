using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MarksApi.Dtos;
using MarksApi.Services.Students;

namespace MarksApi.Endpoints;

public static class StudentEndpoints
{
    public static IEndpointRouteBuilder MapStudentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/students")
            .WithTags("Students");

        group.MapGet("", GetListAsync)
            .RequireAuthorization("TeacherOnly")
            .WithName("ListStudents")
            .Produces<PagedResultDto<StudentResponseDto>>(
                StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:int}", GetByIdAsync)
            .RequireAuthorization("TeacherOrStudent")
            .WithName("GetStudentById")
            .Produces<StudentResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .RequireAuthorization("TeacherOnly")
            .WithName("CreateStudent")
            .Produces<StudentResponseDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetListAsync(
        IStudentService studentService,
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
            "fullName" or "-fullName" or
            "registrationNo" or "-registrationNo" or
            "email" or "-email"))
        {
            errors["sort"] =
            [
                "Use id, fullName, registrationNo or email. " +
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

        var result = await studentService.ListAsync(
            page,
            pageSize,
            search,
            sortValue,
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetByIdAsync(
        int id,
        IStudentService studentService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await studentService.GetByIdAsync(
            id, context.User, cancellationToken);

        if (result.IsForbidden)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "You may only read your own student record.",
                instance: context.Request.Path.Value);
        }

        if (result.Student is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Student not found",
                detail: "The requested student was not found.",
                instance: context.Request.Path.Value);
        }

        return Results.Ok(result.Student);
    }

    private static async Task<IResult> CreateAsync(
        StudentWriteRequestDto request,
        IStudentService studentService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(
                errors,
                detail: "Correct the indicated fields and try again.",
                instance: context.Request.Path.Value);
        }

        var student = await studentService.CreateAsync(
            request, cancellationToken);

        return Results.Created(
            $"/api/students/{student.Id}", student);
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
