using System.Text;
using MarksApi.Configuration;
using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Endpoints;
using MarksApi.Infrastructure;
using MarksApi.Services.Auth;
using MarksApi.Services.Students;
using MarksApi.Services.Courses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Database is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database");

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IDataSeeder, DataSeeder>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<ICourseService, CourseService>();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt")
    .ValidateDataAnnotations()
    .Validate(
        settings =>
            !string.IsNullOrWhiteSpace(settings.Key) &&
            Encoding.UTF8.GetByteCount(settings.Key) >= 32,
        "Jwt:Key must contain at least 32 UTF-8 bytes.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var settings = jwtOptions.Value;

        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(settings.Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TeacherOnly", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("Teacher"));

    options.AddPolicy("TeacherOrStudent", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("Teacher", "Student"));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = true;
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("SeedData"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Demo seeding requires the Development environment.");
    }

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var seeder = scope.ServiceProvider
            .GetRequiredService<IDataSeeder>();

        await seeder.SeedAsync(app.Lifetime.ApplicationStopping);
    }

    await app.DisposeAsync();
    return;
}

app.UseExceptionHandler();

app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    var statusCode = context.Response.StatusCode;

    var detail = statusCode switch
    {
        401 => "A valid bearer token is required.",
        403 => "You do not have permission to access this resource.",
        404 => "The requested resource was not found.",
        405 => "This HTTP method is not allowed for this resource.",
        415 => "Use a supported content type such as application/json.",
        _ => "The request could not be completed."
    };

    await Results.Problem(
        statusCode: statusCode,
        title: ReasonPhrases.GetReasonPhrase(statusCode),
        detail: detail,
        instance: context.Request.Path.Value)
        .ExecuteAsync(context);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Hello World!").AllowAnonymous();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapStudentEndpoints();
app.MapCourseEndpoints();

app.Run();
