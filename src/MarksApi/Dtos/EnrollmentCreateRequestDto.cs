using System.ComponentModel.DataAnnotations;

namespace MarksApi.Dtos;

public sealed class EnrollmentCreateRequestDto
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid student ID is required.")]
    public int StudentId { get; init; }
}
