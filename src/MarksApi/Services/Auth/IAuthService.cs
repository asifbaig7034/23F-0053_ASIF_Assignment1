using MarksApi.Dtos;

namespace MarksApi.Services.Auth;

public interface IAuthService
{
    Task<LoginResponseDto?> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default);
}