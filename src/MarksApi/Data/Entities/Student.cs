using System;
using System.Collections.Generic;

namespace MarksApi.Data.Entities;

public sealed class Student
{
    public int Id { get; set; }
    public required string RegistrationNo { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Enrollment> Enrollments { get; set; }
        = new List<Enrollment>();
}