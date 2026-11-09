namespace MarksApi.Dtos;

public sealed record CourseResponseDto(
    int Id,
    string Code,
    string Title,
    int CreditHours);
