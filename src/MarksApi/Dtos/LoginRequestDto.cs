using System.ComponentModel.DataAnnotations;

namespace MarksApi.Dtos;

public sealed class LoginRequestDto
{
    [Required]
    [StringLength(50)]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}