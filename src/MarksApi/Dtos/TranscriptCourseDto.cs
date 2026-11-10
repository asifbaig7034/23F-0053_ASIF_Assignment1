namespace MarksApi.Dtos;

public sealed record TranscriptCourseDto(
    string Code,
    int CreditHours,
    int? Mark,
    bool Passed);
