using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarksApi.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MarksApi.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", GetHealthAsync)
            .AllowAnonymous()
            .WithName("GetHealth")
            .Produces<HealthResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> GetHealthAsync(
        HealthCheckService healthChecks,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        var report = await healthChecks.CheckHealthAsync(
            cancellationToken);

        if (report.Status != HealthStatus.Healthy)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service unavailable",
                detail: "The API cannot connect to a required service.",
                instance: "/health");
        }

        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString());

        return Results.Ok(
            new HealthResponseDto(report.Status.ToString(), checks));
    }
}