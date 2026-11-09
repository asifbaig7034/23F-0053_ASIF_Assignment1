using MarksApi.Dtos;

namespace MarksApi.Services.Students;

public interface IStudentService
{
    Task<PagedResultDto<StudentResponseDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string sort,
        CancellationToken cancellationToken = default);
}