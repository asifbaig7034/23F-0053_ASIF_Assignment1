namespace MarksApi.Dtos;

public sealed record StudentResponseDto(
    int Id,
    string RegistrationNo,
    string FullName,
    string Email);
