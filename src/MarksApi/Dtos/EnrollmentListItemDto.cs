namespace MarksApi.Dtos;

public sealed record EnrollmentListItemDto(
    int Id,
    int StudentId,
    int CourseId,
    string RegistrationNo,
    string FullName,
    int? Mark,
    DateTime EnrolledAtUtc);
