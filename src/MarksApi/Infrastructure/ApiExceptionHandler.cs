using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
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
        var postgres = exception.GetBaseException() as PostgresException;

        var duplicateStudent = postgres is
        {
            SqlState: "23505",
            ConstraintName: "ux_students_registration_no"
                or "ux_students_email"
        };

        var studentInUse =
            HttpMethods.IsDelete(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/api/students") &&
            postgres is
            {
                SqlState: "23503",
                ConstraintName: "fk_enrollments_students_student_id"
                    or "fk_users_students_student_id"
            };

        var statusCode = duplicateStudent || studentInUse
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
        else if (studentInUse)
        {
            logger.LogWarning(
                "Student deletion blocked at {Path}.",
                context.Request.Path);

            result = Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Student in use",
                detail:
                    "Student cannot be deleted while enrollments or a linked user account exist.",
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
