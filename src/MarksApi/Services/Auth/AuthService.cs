using System.Globalization;
using System.Text;
using MarksApi.Configuration;
using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MarksApi.Services.Auth;

public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<LoginResponseDto?> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => new LoginAccount(
                u.Id,
                u.Username,
                u.PasswordHash,
                u.Role,
                u.StudentId))
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "Login failed for username {Username}.", username);

            return null;
        }

        var user = new User
        {
            Id = account.Id,
            Username = account.Username,
            PasswordHash = account.PasswordHash,
            Role = account.Role,
            StudentId = account.StudentId
        };

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            account.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning(
                "Login failed for username {Username}.", username);

            return null;
        }

        if (verification ==
            PasswordVerificationResult.SuccessRehashNeeded)
        {
            var newHash = passwordHasher.HashPassword(
                user, request.Password);

            await db.Users
                .Where(u =>
                    u.Id == account.Id &&
                    u.PasswordHash == account.PasswordHash)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        u => u.PasswordHash, newHash),
                    cancellationToken);
        }

        var response = CreateToken(user);

        logger.LogInformation(
            "User {Username} logged in with role {Role}.",
            user.Username,
            user.Role);

        return response;
    }

    private LoginResponseDto CreateToken(User user)
    {
        var settings = jwtOptions.Value;

        var now = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;

        var expiresAtUtc = now.AddMinutes(settings.ExpiryMinutes);

        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(CultureInfo.InvariantCulture),
            ["name"] = user.Username,
            ["role"] = user.Role,
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        if (user.StudentId is int studentId)
        {
            claims["studentId"] = studentId.ToString(
                CultureInfo.InvariantCulture);
        }

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(settings.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler()
            .CreateToken(descriptor);

        return new LoginResponseDto(
            token, expiresAtUtc, user.Role);
    }

    private sealed record LoginAccount(
        int Id,
        string Username,
        string PasswordHash,
        string Role,
        int? StudentId);
}