namespace MarksApi.Dtos;

public sealed record EnrollmentResponseDto(
    int Id,
    int StudentId,
    int CourseId,
    int? Mark,
    DateTime EnrolledAtUtc);
