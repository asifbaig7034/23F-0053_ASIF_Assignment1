using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MarksApi.Dtos;
using MarksApi.Services.Auth;

namespace MarksApi.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .WithName("Login")
            .WithTags("Auth")
            .Produces<LoginResponseDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequestDto request,
        IAuthService authService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        if (!isValid)
        {
            var errors = validationResults
                .SelectMany(result => result.MemberNames.Select(
                    member => new
                    {
                        Key = JsonNamingPolicy.CamelCase.ConvertName(member),
                        Message = result.ErrorMessage ?? "Invalid value."
                    }))
                .GroupBy(error => error.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message)
                        .Distinct()
                        .ToArray());

            return Results.ValidationProblem(
                errors,
                detail: "Correct the indicated fields and try again.",
                instance: context.Request.Path.Value);
        }

        var response = await authService.LoginAsync(
            request, cancellationToken);

        if (response is null)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Invalid username or password.",
                instance: context.Request.Path.Value);
        }

        return Results.Ok(response);
    }
}