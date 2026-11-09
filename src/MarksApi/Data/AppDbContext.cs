using MarksApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.UseIdentityByDefaultColumns();

        modelBuilder.Entity<Student>(entity =>
        {
            entity.ToTable("students", table =>
            {
                table.HasCheckConstraint(
                    "ck_students_email_lowercase",
                    "email = lower(email)");
            });

            entity.HasKey(s => s.Id);

            entity.Property(s => s.RegistrationNo)
                .HasMaxLength(20).IsRequired();

            entity.Property(s => s.FullName)
                .HasMaxLength(100).IsRequired();

            entity.Property(s => s.Email)
                .HasMaxLength(150).IsRequired();

            entity.Property(s => s.CreatedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(s => s.RegistrationNo)
                .IsUnique()
                .HasDatabaseName("ux_students_registration_no");

            entity.HasIndex(s => s.Email)
                .IsUnique()
                .HasDatabaseName("ux_students_email");
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.ToTable("courses", table =>
            {
                table.HasCheckConstraint(
                    "ck_courses_credit_hours",
                    "credit_hours BETWEEN 1 AND 4");
            });

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Code)
                .HasMaxLength(10).IsRequired();

            entity.Property(c => c.Title)
                .HasMaxLength(150).IsRequired();

            entity.Property(c => c.CreditHours)
                .IsRequired();

            entity.Property(c => c.CreatedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(c => c.Code)
                .IsUnique()
                .HasDatabaseName("ux_courses_code");
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("enrollments", table =>
            {
                table.HasCheckConstraint(
                    "ck_enrollments_mark",
                    "mark IS NULL OR mark BETWEEN 0 AND 100");
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.EnrolledAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(e => new { e.StudentId, e.CourseId })
                .IsUnique()
                .HasDatabaseName("ux_enrollments_student_course");

            entity.HasOne(e => e.Student)
                .WithMany(s => s.Enrollments)
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Course)
                .WithMany(c => c.Enrollments)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", table =>
            {
                table.HasCheckConstraint(
                    "ck_users_role",
                    "role IN ('Teacher', 'Student')");

                table.HasCheckConstraint(
                    "ck_users_student_link",
                    "role <> 'Student' OR student_id IS NOT NULL");
            });

            entity.HasKey(u => u.Id);

            entity.Property(u => u.Username)
                .HasMaxLength(50).IsRequired();

            entity.Property(u => u.PasswordHash)
                .HasColumnType("text")
                .IsRequired();

            entity.Property(u => u.Role)
                .HasMaxLength(20).IsRequired();

            entity.HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("ux_users_username");

            entity.HasOne(u => u.Student)
                .WithMany()
                .HasForeignKey(u => u.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}