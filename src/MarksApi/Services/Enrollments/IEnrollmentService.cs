using MarksApi.Dtos;

namespace MarksApi.Services.Enrollments;

public interface IEnrollmentService
{
    Task<EnrollmentResponseDto> CreateAsync(
        int courseId,
        int studentId,
        CancellationToken cancellationToken = default);

    Task<PagedResultDto<EnrollmentListItemDto>?> ListAsync(
        int courseId,
        int page,
        int pageSize,
        string? search,
        string sort,
        CancellationToken cancellationToken = default);

    Task<EnrollmentResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<EnrollmentResponseDto?> UpdateMarkAsync(
        int id,
        int mark,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}
