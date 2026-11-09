using MarksApi.Data;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Services.Students;

public sealed class StudentService(
    AppDbContext db) : IStudentService
{
    public async Task<PagedResultDto<StudentResponseDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string sort,
        CancellationToken cancellationToken = default)
    {
        var query = db.Students.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(student =>
                student.FullName.ToLower().Contains(term) ||
                student.RegistrationNo.ToLower().Contains(term) ||
                student.Email.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var orderedQuery = sort switch
        {
            "-id" => query.OrderByDescending(s => s.Id),

            "fullName" => query
                .OrderBy(s => s.FullName)
                .ThenBy(s => s.Id),

            "-fullName" => query
                .OrderByDescending(s => s.FullName)
                .ThenBy(s => s.Id),

            "registrationNo" => query
                .OrderBy(s => s.RegistrationNo)
                .ThenBy(s => s.Id),

            "-registrationNo" => query
                .OrderByDescending(s => s.RegistrationNo)
                .ThenBy(s => s.Id),

            "email" => query
                .OrderBy(s => s.Email)
                .ThenBy(s => s.Id),

            "-email" => query
                .OrderByDescending(s => s.Email)
                .ThenBy(s => s.Id),

            _ => query.OrderBy(s => s.Id)
        };

        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(student => new StudentResponseDto(
                student.Id,
                student.RegistrationNo,
                student.FullName,
                student.Email))
            .ToListAsync(cancellationToken);

        return new PagedResultDto<StudentResponseDto>(
            items, page, pageSize, totalCount);
    }
}