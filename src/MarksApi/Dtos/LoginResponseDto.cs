namespace MarksApi.Dtos;

public sealed record LoginResponseDto(
    string Token,
    DateTime ExpiresAtUtc,
    string Role);