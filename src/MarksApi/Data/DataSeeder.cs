using System;
using System.Threading;
using System.Threading.Tasks;
using MarksApi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarksApi.Data;

public sealed class DataSeeder(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    IConfiguration configuration,
    ILogger<DataSeeder> logger) : IDataSeeder
{
    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize simultaneous runs of this initialization command.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(5134001);",
            cancellationToken);

        if (await db.Users.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Development seed data already exists.");
            return;
        }

        var ali = new Student
        {
            RegistrationNo = "CS-2022-001",
            FullName = "Ali Raza",
            Email = "ali.raza@example.com"
        };

        var sara = new Student
        {
            RegistrationNo = "CS-2022-002",
            FullName = "Sara Khan",
            Email = "sara.khan@example.com"
        };

        var programming = new Course
        {
            Code = "CS401",
            Title = "Advanced Programming",
            CreditHours = 3
        };

        var mathematics = new Course
        {
            Code = "MA201",
            Title = "Mathematics",
            CreditHours = 2
        };

        var artificialIntelligence = new Course
        {
            Code = "AI301",
            Title = "Artificial Intelligence",
            CreditHours = 3
        };

        db.Students.AddRange(ali, sara);
        db.Courses.AddRange(
            programming, mathematics, artificialIntelligence);

        await db.SaveChangesAsync(cancellationToken);

        db.Enrollments.AddRange(
            new Enrollment
            {
                StudentId = ali.Id,
                CourseId = programming.Id,
                Mark = 80
            },
            new Enrollment
            {
                StudentId = ali.Id,
                CourseId = mathematics.Id,
                Mark = 60
            },
            new Enrollment
            {
                StudentId = ali.Id,
                CourseId = artificialIntelligence.Id,
                Mark = null
            },
            new Enrollment
            {
                StudentId = sara.Id,
                CourseId = programming.Id,
                Mark = 70
            },
            new Enrollment
            {
                StudentId = sara.Id,
                CourseId = mathematics.Id,
                Mark = 90
            },
            new Enrollment
            {
                StudentId = sara.Id,
                CourseId = artificialIntelligence.Id,
                Mark = 40
            });

        db.Users.AddRange(
            CreateUser("Teacher", "Teacher", null),
            CreateUser("Student1", "Student", ali.Id),
            CreateUser("Student2", "Student", sara.Id));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Development seed data created: " +
            "2 students, 3 courses, 6 enrollments and 3 users.");
    }

    private User CreateUser(
        string account,
        string role,
        int? studentId)
    {
        var user = new User
        {
            Username = Required($"Seed:{account}:Username"),
            PasswordHash = string.Empty,
            Role = role,
            StudentId = studentId
        };

        user.PasswordHash = passwordHasher.HashPassword(
            user, Required($"Seed:{account}:Password"));

        return user;
    }

    private string Required(string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing configuration: {key}");
        }

        return value;
    }
}