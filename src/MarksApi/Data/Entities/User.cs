namespace MarksApi.Data.Entities;

public sealed class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public int? StudentId { get; set; }

    public Student? Student { get; set; }
}