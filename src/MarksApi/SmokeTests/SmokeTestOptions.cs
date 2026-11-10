using System.ComponentModel.DataAnnotations;

namespace MarksApi.SmokeTests;

public sealed class SmokeTestOptions
{
    [Required] public string TeacherUsername { get; set; } = string.Empty;
    [Required] public string TeacherPassword { get; set; } = string.Empty;
    [Required] public string StudentUsername { get; set; } = string.Empty;
    [Required] public string StudentPassword { get; set; } = string.Empty;
}
