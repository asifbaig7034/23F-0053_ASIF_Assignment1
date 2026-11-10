using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Services.Enrollments;

public sealed class EnrollmentService(
    AppDbContext db,
    ILogger<EnrollmentService> logger) : IEnrollmentService
{
    public async Task<EnrollmentResponseDto> CreateAsync(
        int courseId,
        int studentId,
        CancellationToken cancellationToken = default)
    {
        var enrollment = new Enrollment
        {
            StudentId = studentId,
            CourseId = courseId,
            Mark = null,
            EnrolledAtUtc = DateTime.UtcNow
        };

        db.Enrollments.Add(enrollment);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Enrolled student {StudentId} in course {CourseId}; enrollment {EnrollmentId}.",
            studentId,
            courseId,
            enrollment.Id);

        return new EnrollmentResponseDto(
            enrollment.Id,
            enrollment.StudentId,
            enrollment.CourseId,
            enrollment.Mark,
            enrollment.EnrolledAtUtc);
    }

    public async Task<PagedResultDto<EnrollmentListItemDto>?> ListAsync(
        int courseId,
        int page,
        int pageSize,
        string? search,
        string sort,
        CancellationToken cancellationToken = default)
    {
        var courseExists = await db.Courses
            .AsNoTracking()
            .AnyAsync(course => course.Id == courseId, cancellationToken);

        if (!courseExists)
        {
            return null;
        }

        var query = db.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.CourseId == courseId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(enrollment =>
                enrollment.Student.RegistrationNo.ToLower().Contains(term) ||
                enrollment.Student.FullName.ToLower().Contains(term) ||
                enrollment.Student.Email.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var orderedQuery = sort switch
        {
            "-id" => query.OrderByDescending(e => e.Id),

            "studentId" => query
                .OrderBy(e => e.StudentId)
                .ThenBy(e => e.Id),

            "-studentId" => query
                .OrderByDescending(e => e.StudentId)
                .ThenBy(e => e.Id),

            "fullName" => query
                .OrderBy(e => e.Student.FullName)
                .ThenBy(e => e.Id),

            "-fullName" => query
                .OrderByDescending(e => e.Student.FullName)
                .ThenBy(e => e.Id),

            "mark" => query
                .OrderBy(e => e.Mark == null)
                .ThenBy(e => e.Mark)
                .ThenBy(e => e.Id),

            "-mark" => query
                .OrderBy(e => e.Mark == null)
                .ThenByDescending(e => e.Mark)
                .ThenBy(e => e.Id),

            "enrolledAtUtc" => query
                .OrderBy(e => e.EnrolledAtUtc)
                .ThenBy(e => e.Id),

            "-enrolledAtUtc" => query
                .OrderByDescending(e => e.EnrolledAtUtc)
                .ThenBy(e => e.Id),

            _ => query.OrderBy(e => e.Id)
        };

        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(enrollment => new EnrollmentListItemDto(
                enrollment.Id,
                enrollment.StudentId,
                enrollment.CourseId,
                enrollment.Student.RegistrationNo,
                enrollment.Student.FullName,
                enrollment.Mark,
                enrollment.EnrolledAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResultDto<EnrollmentListItemDto>(
            items, page, pageSize, totalCount);
    }

    public async Task<EnrollmentResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await db.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.Id == id)
            .Select(enrollment => new EnrollmentResponseDto(
                enrollment.Id,
                enrollment.StudentId,
                enrollment.CourseId,
                enrollment.Mark,
                enrollment.EnrolledAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EnrollmentResponseDto?> UpdateMarkAsync(
        int id,
        int mark,
        CancellationToken cancellationToken = default)
    {
        var enrollment = await db.Enrollments
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (enrollment is null)
        {
            return null;
        }

        enrollment.Mark = mark;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        logger.LogInformation(
            "Updated enrollment {EnrollmentId} mark to {Mark}.",
            id,
            mark);

        return new EnrollmentResponseDto(
            enrollment.Id,
            enrollment.StudentId,
            enrollment.CourseId,
            enrollment.Mark,
            enrollment.EnrolledAtUtc);
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await db.Enrollments
            .Where(enrollment => enrollment.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        if (affectedRows == 0)
        {
            return false;
        }

        logger.LogInformation(
            "Deleted enrollment {EnrollmentId}.", id);

        return true;
    }
}
