using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace MarksApi.Infrastructure;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        if (statusCode >= 500)
        {
            logger.LogError(
                exception,
                "Request failed: {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }

        IResult result;

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            result = Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["body"] =
                    [
                        "The request body or parameters are invalid."
                    ]
                },
                detail: "Provide valid JSON and valid parameter values.",
                instance: context.Request.Path.Value);
        }
        else
        {
            result = Results.Problem(
                statusCode: statusCode,
                title: ReasonPhrases.GetReasonPhrase(statusCode),
                detail: statusCode >= 500
                    ? "An unexpected error occurred."
                    : "The request could not be processed.",
                instance: context.Request.Path.Value);
        }

        await result.ExecuteAsync(context);
        return true;
    }
}