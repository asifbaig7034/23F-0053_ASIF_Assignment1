using MarksApi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using MarksApi.Endpoints;
using MarksApi.Data;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Database is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database");
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IDataSeeder, DataSeeder>();

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

app.MapGet("/", () => "Hello World!");
app.MapHealthEndpoints();
app.Run();
