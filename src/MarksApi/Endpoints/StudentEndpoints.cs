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
}