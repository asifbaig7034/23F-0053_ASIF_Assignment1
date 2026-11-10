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

        var duplicateCourse = postgres is
        {
            SqlState: "23505",
            ConstraintName: "ux_courses_code"
        };

        var duplicateEnrollment = postgres is
        {
            SqlState: "23505",
            ConstraintName: "ux_enrollments_student_course"
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

        var courseInUse =
            HttpMethods.IsDelete(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/api/courses") &&
            postgres is
            {
                SqlState: "23503",
                ConstraintName: "fk_enrollments_courses_course_id"
            };

        var enrollmentParentMissing =
            HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/api/courses") &&
            postgres is
            {
                SqlState: "23503",
                ConstraintName: "fk_enrollments_students_student_id"
                    or "fk_enrollments_courses_course_id"
            };

        var conflict =
            duplicateStudent || duplicateCourse || duplicateEnrollment ||
            studentInUse || courseInUse;

        var statusCode = enrollmentParentMissing
            ? StatusCodes.Status404NotFound
            : conflict
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
        else if (conflict || enrollmentParentMissing)
        {
            logger.LogWarning(
                "Request rejected with status {StatusCode}: {Method} {Path}.",
                statusCode,
                context.Request.Method,
                context.Request.Path);
        }

        IResult result;

        if (duplicateStudent)
        {
            result = Problem(
                context, StatusCodes.Status409Conflict,
                "Duplicate student",
                "A student with this registration number or email already exists.");
        }
        else if (duplicateCourse)
        {
            result = Problem(
                context, StatusCodes.Status409Conflict,
                "Duplicate course",
                "A course with this code already exists.");
        }
        else if (duplicateEnrollment)
        {
            result = Problem(
                context, StatusCodes.Status409Conflict,
                "Duplicate enrollment",
                "Student is already enrolled in this course.");
        }
        else if (studentInUse)
        {
            result = Problem(
                context, StatusCodes.Status409Conflict,
                "Student in use",
                "Student cannot be deleted while enrollments or a linked user account exist.");
        }
        else if (courseInUse)
        {
            result = Problem(
                context, StatusCodes.Status409Conflict,
                "Course in use",
                "Course cannot be deleted while enrollments exist.");
        }
        else if (enrollmentParentMissing)
        {
            result = Problem(
                context, StatusCodes.Status404NotFound,
                "Student or course not found",
                "The specified student or course was not found.");
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
            result = Problem(
                context,
                statusCode,
                ReasonPhrases.GetReasonPhrase(statusCode),
                statusCode >= 500
                    ? "An unexpected error occurred."
                    : "The request could not be processed.");
        }

        await result.ExecuteAsync(context);
        return true;
    }

    private static IResult Problem(
        HttpContext context,
        int statusCode,
        string title,
        string detail)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            instance: context.Request.Path.Value);
    }
}
