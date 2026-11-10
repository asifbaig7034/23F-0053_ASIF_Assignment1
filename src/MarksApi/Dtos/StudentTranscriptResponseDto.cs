namespace MarksApi.Dtos;

public sealed record StudentTranscriptResponseDto(
    int StudentId,
    string RegistrationNo,
    string FullName,
    IReadOnlyList<TranscriptCourseDto> Courses,
    decimal? WeightedAverage);
