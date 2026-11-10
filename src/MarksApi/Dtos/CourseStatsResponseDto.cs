namespace MarksApi.Dtos;

public sealed record CourseStatsResponseDto(
    int CourseId,
    string CourseCode,
    int Enrolled,
    int Graded,
    decimal? Average,
    int? Highest,
    int? Lowest,
    int Passed);
