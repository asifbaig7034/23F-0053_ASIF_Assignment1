using System.ComponentModel.DataAnnotations;

namespace MarksApi.Dtos;

public sealed class CourseWriteRequestDto
{
    [Required(ErrorMessage = "Course code is required.")]
    [StringLength(10)]
    public string Code { get; init; } = string.Empty;

    [Required(ErrorMessage = "Course title is required.")]
    [StringLength(150)]
    public string Title { get; init; } = string.Empty;

    [Range(1, 4, ErrorMessage = "Credit hours must be between 1 and 4.")]
    public int CreditHours { get; init; }
}
