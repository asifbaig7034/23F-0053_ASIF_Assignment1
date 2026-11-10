using System.ComponentModel.DataAnnotations;

namespace MarksApi.Configuration;

public sealed class GradingOptions
{
    [Range(0, 100)]
    public int PassMark { get; set; } = 50;
}
