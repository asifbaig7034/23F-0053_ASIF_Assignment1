using System.ComponentModel.DataAnnotations;

namespace MarksApi.Configuration;

public sealed class JwtOptions
{
    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 60;
}