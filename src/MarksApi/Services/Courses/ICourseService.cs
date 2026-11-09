using MarksApi.Dtos;

namespace MarksApi.Services.Courses;

public interface ICourseService
{
    Task<PagedResultDto<CourseResponseDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string sort,
        CancellationToken cancellationToken = default);

    Task<CourseResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<CourseResponseDto> CreateAsync(
        CourseWriteRequestDto request,
        CancellationToken cancellationToken = default);

    Task<CourseResponseDto?> UpdateAsync(
        int id,
        CourseWriteRequestDto request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}
