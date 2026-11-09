using System;
using System.Collections.Generic;

namespace MarksApi.Data.Entities;

public sealed class Course
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Title { get; set; }
    public int CreditHours { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Enrollment> Enrollments { get; set; }
        = new List<Enrollment>();
}