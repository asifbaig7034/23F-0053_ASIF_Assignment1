using System.ComponentModel.DataAnnotations;

namespace MarksApi.Dtos;

public sealed class StudentWriteRequestDto
{
    [Required(ErrorMessage = "Registration number is required.")]
    [StringLength(20)]
    public string RegistrationNo { get; init; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100)]
    public string FullName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [StringLength(150)]
    [EmailAddress(ErrorMessage = "Provide a valid email address.")]
    public string Email { get; init; } = string.Empty;
}
