using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarksApi.Infrastructure;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var duplicateStudent = exception is DbUpdateException
        {
            InnerException: PostgresException
            {
                SqlState: "23505",
                ConstraintName: "ux_students_registration_no"
                    or "ux_students_email"
            }
        };

        var statusCode = duplicateStudent
            ? StatusCodes.Status409Conflict
            : exception is BadHttpRequestException badRequest
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

        if (duplicateStudent)
        {
            logger.LogWarning(
                "Duplicate student rejected at {Path}.",
                context.Request.Path);

            result = Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate student",
                detail:
                    "A student with this registration number or email already exists.",
                instance: context.Request.Path.Value);
        }
        else if (statusCode == StatusCodes.Status400BadRequest)
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
