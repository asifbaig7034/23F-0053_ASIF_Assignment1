using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Services.Courses;

public sealed class CourseService(
    AppDbContext db,
    ILogger<CourseService> logger) : ICourseService
{
    public async Task<PagedResultDto<CourseResponseDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string sort,
        CancellationToken cancellationToken = default)
    {
        var query = db.Courses.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(course =>
                course.Code.ToLower().Contains(term) ||
                course.Title.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var orderedQuery = sort switch
        {
            "-id" => query.OrderByDescending(c => c.Id),

            "code" => query
                .OrderBy(c => c.Code)
                .ThenBy(c => c.Id),

            "-code" => query
                .OrderByDescending(c => c.Code)
                .ThenBy(c => c.Id),

            "title" => query
                .OrderBy(c => c.Title)
                .ThenBy(c => c.Id),

            "-title" => query
                .OrderByDescending(c => c.Title)
                .ThenBy(c => c.Id),

            "creditHours" => query
                .OrderBy(c => c.CreditHours)
                .ThenBy(c => c.Id),

            "-creditHours" => query
                .OrderByDescending(c => c.CreditHours)
                .ThenBy(c => c.Id),

            _ => query.OrderBy(c => c.Id)
        };

        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(course => new CourseResponseDto(
                course.Id,
                course.Code,
                course.Title,
                course.CreditHours))
            .ToListAsync(cancellationToken);

        return new PagedResultDto<CourseResponseDto>(
            items, page, pageSize, totalCount);
    }

    public async Task<CourseResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await db.Courses
            .AsNoTracking()
            .Where(course => course.Id == id)
            .Select(course => new CourseResponseDto(
                course.Id,
                course.Code,
                course.Title,
                course.CreditHours))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CourseResponseDto> CreateAsync(
        CourseWriteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var course = new Course
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Title = request.Title.Trim(),
            CreditHours = request.CreditHours,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Courses.Add(course);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created course {CourseId} with code {CourseCode}.",
            course.Id,
            course.Code);

        return new CourseResponseDto(
            course.Id,
            course.Code,
            course.Title,
            course.CreditHours);
    }

    public async Task<CourseResponseDto?> UpdateAsync(
        int id,
        CourseWriteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var title = request.Title.Trim();
        var creditHours = request.CreditHours;

        var affectedRows = await db.Courses
            .Where(course => course.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(course => course.Code, code)
                    .SetProperty(course => course.Title, title)
                    .SetProperty(course => course.CreditHours, creditHours),
                cancellationToken);

        if (affectedRows == 0)
        {
            return null;
        }

        logger.LogInformation("Updated course {CourseId}.", id);

        return new CourseResponseDto(
            id, code, title, creditHours);
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await db.Courses
            .Where(course => course.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        if (affectedRows == 0)
        {
            return false;
        }

        logger.LogInformation("Deleted course {CourseId}.", id);

        return true;
    }
}
