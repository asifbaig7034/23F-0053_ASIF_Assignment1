using System.Security.Claims;
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

    Task<StudentLookupResult> GetByIdAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken = default);

    Task<StudentResponseDto> CreateAsync(
        StudentWriteRequestDto request,
        CancellationToken cancellationToken = default);

    Task<StudentResponseDto?> UpdateAsync(
        int id,
        StudentWriteRequestDto request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}

public sealed record StudentLookupResult(
    StudentResponseDto? Student,
    bool IsForbidden);
