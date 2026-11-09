using System.Globalization;
using System.Security.Claims;
using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Services.Students;

public sealed class StudentService(
    AppDbContext db,
    ILogger<StudentService> logger) : IStudentService
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

    public async Task<StudentLookupResult> GetByIdAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken = default)
    {
        if (!caller.IsInRole("Teacher"))
        {
            var studentIdClaim = caller.FindFirst("studentId")?.Value;

            if (!caller.IsInRole("Student") ||
                !int.TryParse(
                    studentIdClaim,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ownStudentId) ||
                ownStudentId != id)
            {
                return new StudentLookupResult(
                    null, IsForbidden: true);
            }
        }

        var student = await db.Students
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new StudentResponseDto(
                s.Id,
                s.RegistrationNo,
                s.FullName,
                s.Email))
            .SingleOrDefaultAsync(cancellationToken);

        return new StudentLookupResult(
            student, IsForbidden: false);
    }

    public async Task<StudentResponseDto> CreateAsync(
        StudentWriteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var student = new Student
        {
            RegistrationNo = request.RegistrationNo.Trim(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Students.Add(student);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created student {StudentId} with registration number {RegistrationNo}.",
            student.Id,
            student.RegistrationNo);

        return new StudentResponseDto(
            student.Id,
            student.RegistrationNo,
            student.FullName,
            student.Email);
    }
}
