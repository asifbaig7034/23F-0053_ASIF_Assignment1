using System.ComponentModel.DataAnnotations;

namespace MarksApi.Dtos;

public sealed class MarkUpdateRequestDto
{
    [Required(ErrorMessage = "Mark is required.")]
    [Range(0, 100, ErrorMessage = "Mark must be a whole number from 0 to 100.")]
    public int? Mark { get; init; }
}
