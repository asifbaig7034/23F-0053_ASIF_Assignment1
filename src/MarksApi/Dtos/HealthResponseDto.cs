using System.Collections.Generic;

namespace MarksApi.Dtos;

public sealed record HealthResponseDto(
    string Status,
    IReadOnlyDictionary<string, string> Checks);