using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MarksApi.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MarksApi.SmokeTests;

public sealed class SmokeTestRunner(
    IOptions<SmokeTestOptions> credentials,
    IOptions<GradingOptions> grading) : IDisposable
{
    private readonly HttpClient client = new(
        new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly List<string> cleanupPaths = [];
    private CancellationToken runToken;
    private string teacherToken = string.Empty;
    private int passed;
    private int failed;

    public async Task<int> RunAsync(
        Uri address,
        CancellationToken cancellationToken)
    {
        client.BaseAddress = address;

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        runToken = timeout.Token;

        try
        {
            await RunChecksAsync();
        }
        catch (Exception exception)
        {
            failed++;
            Console.WriteLine(
                $"FAIL setup/run: {exception.GetType().Name}");
        }
        finally
        {
            using var cleanupTimeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(60));

            foreach (var path in cleanupPaths.AsEnumerable().Reverse().ToArray())
            {
                await CheckAsync(
                    "DELETE", path, 204, teacherToken,
                    cancellationToken: cleanupTimeout.Token,
                    allowMissing: true);

                await CheckAsync(
                    "GET", path, 404, teacherToken,
                    cancellationToken: cleanupTimeout.Token);
            }
        }

        Console.WriteLine(
            $"SMOKE SUMMARY: {passed} passed, {failed} failed");

        return failed == 0 ? 0 : 1;
    }

    private async Task RunChecksAsync()
    {
        var login = credentials.Value;
        var passMark = grading.Value.PassMark;

        await CheckAsync("GET", "/health", 200, verify: r =>
            r.Text("status") == "Healthy" &&
            r.Json.GetProperty("checks")
                .GetProperty("database").GetString() == "Healthy");

        var teacher = await CheckAsync(
            "POST", "/api/auth/login", 200,
            body: new
            {
                username = login.TeacherUsername,
                password = login.TeacherPassword
            },
            verify: r => ValidLogin(r, "Teacher"));

        teacherToken = teacher.Text("token");

        var student = await CheckAsync(
            "POST", "/api/auth/login", 200,
            body: new
            {
                username = login.StudentUsername,
                password = login.StudentPassword
            },
            verify: r => ValidLogin(r, "Student"));

        var studentToken = student.Text("token");
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(studentToken);
        var claim = jwt.Claims.FirstOrDefault(c => c.Type == "studentId")?.Value;

        if (!int.TryParse(claim, out var ownStudentId) || ownStudentId <= 0)
            throw new InvalidOperationException(
                "Student login has no studentId claim.");

        await CheckAsync("POST", "/api/auth/login", 400,
            body: new { username = "", password = "" });

        await CheckAsync("POST", "/api/auth/login", 401,
            body: new
            {
                username = login.TeacherUsername,
                password = "SMOKE-invalid-" + Guid.NewGuid()
            });

        await CheckAsync(
            "GET", $"/api/students/{ownStudentId}", 200, studentToken,
            verify: r => r.Int("id") == ownStudentId);

        await CheckAsync(
            "GET", $"/api/students/{ownStudentId}/transcript",
            200, studentToken,
            verify: r => r.Int("studentId") == ownStudentId);

        var suffix = Guid.NewGuid().ToString("N")[..14];
        var registration = "SMOKE-" + suffix;
        var email = "smoke." + suffix + "@example.com";
        var codeA = ("SMOKE" + suffix[..4] + "A").ToUpperInvariant();
        var codeB = ("SMOKE" + suffix[..4] + "B").ToUpperInvariant();

        var studentBody = new
        {
            registrationNo = registration,
            fullName = "SMOKE Student",
            email = email.ToUpperInvariant()
        };

        var courseBody = new
        {
            code = codeA.ToLowerInvariant(),
            title = "SMOKE Course A",
            creditHours = 2
        };

        var studentId = await CreateAsync(
            "/api/students", studentBody, "/api/students/",
            r => r.Text("registrationNo") == registration &&
                 r.Text("email") == email);

        var studentPath = $"/api/students/{studentId}";

        await CheckAsync(
            "GET",
            $"/api/students?search={registration}&page=1&pageSize=10&sort=id",
            200, teacherToken,
            verify: r => OneItem(r, studentId));

        await CheckAsync("GET", studentPath, 200, teacherToken,
            verify: r => r.Int("id") == studentId &&
                         r.Text("registrationNo") == registration);

        await CheckAsync("PUT", studentPath, 200, teacherToken,
            new
            {
                registrationNo = registration,
                fullName = "SMOKE Student Updated",
                email
            },
            r => r.Int("id") == studentId &&
                 r.Text("fullName") == "SMOKE Student Updated");

        var courseA = await CreateAsync(
            "/api/courses", courseBody, "/api/courses/",
            r => r.Text("code") == codeA && r.Int("creditHours") == 2);

        var courseB = await CreateAsync(
            "/api/courses",
            new { code = codeB, title = "SMOKE Course B", creditHours = 2 },
            "/api/courses/",
            r => r.Text("code") == codeB && r.Int("creditHours") == 2);

        var coursePath = $"/api/courses/{courseA}";
        var enrollmentsPath = coursePath + "/enrollments";
        var statsPath = coursePath + "/stats";
        var transcriptPath = studentPath + "/transcript";

        await CheckAsync(
            "GET", $"/api/courses?search={codeA}&page=1&pageSize=10&sort=id",
            200, teacherToken,
            verify: r => OneItem(r, courseA));

        await CheckAsync(
            "GET", $"/api/courses?search={codeA}", 200, studentToken,
            verify: r => OneItem(r, courseA));

        await CheckAsync("GET", coursePath, 200, teacherToken,
            verify: r => r.Int("id") == courseA && r.Text("code") == codeA);

        await CheckAsync("GET", coursePath, 200, studentToken,
            verify: r => r.Int("id") == courseA);

        await CheckAsync("PUT", coursePath, 200, teacherToken,
            new
            {
                code = codeA,
                title = "SMOKE Course Updated",
                creditHours = 3
            },
            r => r.Text("title") == "SMOKE Course Updated" &&
                 r.Int("creditHours") == 3);

        await CheckAsync("GET", statsPath, 200, teacherToken,
            verify: r => r.Int("enrolled") == 0 &&
                         r.Int("graded") == 0 &&
                         r.IsNull("average") &&
                         r.IsNull("highest") &&
                         r.IsNull("lowest") &&
                         r.Int("passed") == 0);

        await CheckAsync("GET", transcriptPath, 200, teacherToken,
            verify: r => r.Json.GetProperty("courses").GetArrayLength() == 0 &&
                         r.IsNull("weightedAverage"));

        var enrollmentA = await CreateAsync(
            enrollmentsPath, new { studentId }, "/api/enrollments/",
            r => r.Int("studentId") == studentId &&
                 r.Int("courseId") == courseA &&
                 r.IsNull("mark") &&
                 r.Text("enrolledAtUtc").EndsWith(
                     "Z", StringComparison.Ordinal));

        var enrollmentPath = $"/api/enrollments/{enrollmentA}";
        var markPath = enrollmentPath + "/mark";

        await CheckAsync(
            "GET", enrollmentsPath + "?page=1&pageSize=10&sort=id",
            200, teacherToken,
            verify: r => OneItem(r, enrollmentA));

        await CheckAsync("GET", enrollmentPath, 200, teacherToken,
            verify: r => r.Int("id") == enrollmentA && r.IsNull("mark"));

        await CheckAsync("GET", statsPath, 200, teacherToken,
            verify: r => r.Int("enrolled") == 1 &&
                         r.Int("graded") == 0 &&
                         r.IsNull("average"));

        await CheckAsync("GET", transcriptPath, 200, teacherToken,
            verify: r => r.Json.GetProperty("courses").GetArrayLength() == 1 &&
                         r.IsNull("weightedAverage") &&
                         r.Json.GetProperty("courses")[0]
                             .GetProperty("mark").ValueKind == JsonValueKind.Null &&
                         !r.Json.GetProperty("courses")[0]
                             .GetProperty("passed").GetBoolean());

        foreach (var mark in new[] { 0, 100, 80 })
        {
            await CheckAsync("PUT", markPath, 200, teacherToken,
                new { mark }, r => r.Int("mark") == mark);

            await CheckAsync("GET", statsPath, 200, teacherToken,
                verify: r => r.Int("graded") == 1 &&
                             r.Decimal("average") == mark &&
                             r.Int("highest") == mark &&
                             r.Int("lowest") == mark &&
                             r.Int("passed") == (mark >= passMark ? 1 : 0));

            if (mark == 0)
                await CheckAsync("GET", transcriptPath, 200, teacherToken,
                    verify: r => r.Decimal("weightedAverage") == 0m);
        }

        var enrollmentB = await CreateAsync(
            $"/api/courses/{courseB}/enrollments",
            new { studentId }, "/api/enrollments/",
            r => r.Int("studentId") == studentId &&
                 r.Int("courseId") == courseB);

        await CheckAsync(
            "PUT", $"/api/enrollments/{enrollmentB}/mark", 200, teacherToken,
            new { mark = 40 }, r => r.Int("mark") == 40);

        await CheckAsync("GET", transcriptPath, 200, teacherToken,
            verify: r => r.Int("studentId") == studentId &&
                         r.Decimal("weightedAverage") == 64m &&
                         r.Json.GetProperty("courses").GetArrayLength() == 2 &&
                         TranscriptCourse(r, codeA, 3, 80, passMark) &&
                         TranscriptCourse(r, codeB, 2, 40, passMark));

        await CheckAsync("POST", "/api/students", 409, teacherToken,
            new
            {
                registrationNo = registration,
                fullName = "SMOKE Duplicate",
                email = "duplicate." + email
            });

        await CheckAsync("POST", "/api/students", 409, teacherToken,
            new
            {
                registrationNo = "SMOKE-X" + suffix[..12],
                fullName = "SMOKE Duplicate",
                email = email.ToUpperInvariant()
            });

        await CheckAsync("POST", "/api/students", 400, teacherToken,
            new { registrationNo = "", fullName = "", email = "invalid" });

        await CheckAsync(
            "POST", "/api/courses", 409, teacherToken, courseBody);

        await CheckAsync("POST", "/api/courses", 400, teacherToken,
            new { code = codeA, title = "SMOKE Invalid", creditHours = 5 });

        await CheckAsync(
            "POST", enrollmentsPath, 409, teacherToken, new { studentId });

        await CheckAsync(
            "POST", enrollmentsPath, 404, teacherToken,
            new { studentId = int.MaxValue });

        await CheckAsync(
            "POST", $"/api/courses/{int.MaxValue}/enrollments",
            404, teacherToken, new { studentId });

        foreach (object body in new object[]
        {
            new { mark = 101 },
            new { mark = -1 },
            new { mark = 0.5m },
            new { mark = (int?)null },
            new { }
        })
        {
            await CheckAsync("PUT", markPath, 400, teacherToken, body);
        }

        await CheckAsync("DELETE", studentPath, 409, teacherToken);
        await CheckAsync("DELETE", coursePath, 409, teacherToken);

        var protectedRequests =
            new (string Method, string Path, object? Body, bool StudentAllowed)[]
        {
            ("GET", "/api/students", null, false),
            ("GET", studentPath, null, false),
            ("POST", "/api/students", studentBody, false),
            ("PUT", studentPath, studentBody, false),
            ("DELETE", studentPath, null, false),
            ("GET", "/api/courses", null, true),
            ("GET", coursePath, null, true),
            ("POST", "/api/courses", courseBody, false),
            ("PUT", coursePath, courseBody, false),
            ("DELETE", coursePath, null, false),
            ("POST", enrollmentsPath, new { studentId }, false),
            ("GET", enrollmentsPath, null, false),
            ("GET", enrollmentPath, null, false),
            ("PUT", markPath, new { mark = 80 }, false),
            ("DELETE", enrollmentPath, null, false),
            ("GET", statsPath, null, false),
            ("GET", transcriptPath, null, false)
        };

        foreach (var request in protectedRequests)
        {
            await CheckAsync(
                request.Method, request.Path, 401, body: request.Body);

            if (!request.StudentAllowed)
                await CheckAsync(
                    request.Method, request.Path, 403,
                    studentToken, request.Body);
        }

        var missing = int.MaxValue;

        foreach (var path in new[]
        {
            $"/api/students/{missing}",
            $"/api/courses/{missing}",
            $"/api/enrollments/{missing}",
            $"/api/courses/{missing}/enrollments",
            $"/api/courses/{missing}/stats",
            $"/api/students/{missing}/transcript"
        })
        {
            await CheckAsync("GET", path, 404, teacherToken);
        }

        await CheckAsync(
            "PUT", $"/api/students/{missing}", 404, teacherToken, studentBody);

        await CheckAsync(
            "PUT", $"/api/courses/{missing}", 404, teacherToken, courseBody);

        await CheckAsync(
            "PUT", $"/api/enrollments/{missing}/mark",
            404, teacherToken, new { mark = 80 });

        foreach (var path in new[]
        {
            $"/api/students/{missing}",
            $"/api/courses/{missing}",
            $"/api/enrollments/{missing}"
        })
        {
            await CheckAsync("DELETE", path, 404, teacherToken);
        }

        foreach (var path in new[]
        {
            "/api/students?page=0",
            "/api/courses?pageSize=51",
            enrollmentsPath + "?pageSize=51"
        })
        {
            await CheckAsync("GET", path, 400, teacherToken);
        }

        foreach (var path in new[]
        {
            enrollmentPath,
            $"/api/enrollments/{enrollmentB}",
            studentPath,
            coursePath,
            $"/api/courses/{courseB}"
        })
        {
            var deletion = await CheckAsync(
                "DELETE", path, 204, teacherToken);

            var absence = await CheckAsync(
                "GET", path, 404, teacherToken);

            if (deletion.Status == 204 && absence.Status == 404)
                cleanupPaths.Remove(path);
        }
    }

    private async Task<int> CreateAsync(
        string path,
        object body,
        string resourcePrefix,
        Func<Reply, bool> verify)
    {
        var reply = await CheckAsync(
            "POST", path, 201, teacherToken, body,
            r => r.Int("id") > 0 &&
                 r.Location == resourcePrefix + r.Int("id") &&
                 verify(r));

        var id = 0;

        if (reply.Status == 201 &&
            reply.Json.ValueKind == JsonValueKind.Object &&
            reply.Json.TryGetProperty("id", out var value))
        {
            value.TryGetInt32(out id);
        }

        if (reply.Status == 201 &&
            id <= 0 &&
            reply.Location is string location &&
            location.StartsWith(resourcePrefix, StringComparison.Ordinal))
        {
            int.TryParse(location[resourcePrefix.Length..], out id);
        }

        if (reply.Status != 201 || id <= 0)
            throw new InvalidOperationException("Fixture creation failed.");

        cleanupPaths.Add(resourcePrefix + id);
        return id;
    }

    private async Task<Reply> CheckAsync(
        string method,
        string path,
        int expected,
        string? token = null,
        object? body = null,
        Func<Reply, bool>? verify = null,
        CancellationToken? cancellationToken = null,
        bool allowMissing = false)
    {
        var actual = 0;
        var reply = new Reply(0, default, null);

        try
        {
            var ct = cancellationToken ?? runToken;
            using var request = new HttpRequestMessage(
                new HttpMethod(method), path);

            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

            if (body is not null)
                request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request, ct);
            actual = (int)response.StatusCode;

            reply = new Reply(
                actual, default, response.Headers.Location?.ToString());

            var text = await response.Content.ReadAsStringAsync(ct);
            var json = default(JsonElement);

            if (!string.IsNullOrWhiteSpace(text))
            {
                using var document = JsonDocument.Parse(text);
                json = document.RootElement.Clone();
            }

            reply = reply with { Json = json };

            var acceptedMissing = allowMissing && actual == 404;
            var ok = actual == expected || acceptedMissing;

            if (actual == 204)
            {
                ok &= text.Length == 0;
            }
            else
            {
                var mediaType =
                    response.Content.Headers.ContentType?.MediaType
                    ?? string.Empty;

                ok &= (
                    mediaType == "application/json" ||
                    mediaType.EndsWith(
                        "+json", StringComparison.OrdinalIgnoreCase)
                ) && json.ValueKind == JsonValueKind.Object;
            }

            if (ok && actual >= 400)
            {
                ok = reply.Int("status") == actual &&
                     !string.IsNullOrWhiteSpace(reply.Text("title")) &&
                     !string.IsNullOrWhiteSpace(reply.Text("detail"));
            }

            if (ok && actual == 400)
                ok = json.GetProperty("errors").ValueKind ==
                     JsonValueKind.Object;

            if (ok && verify is not null)
                ok = verify(reply);

            Record(
                method, path,
                allowMissing ? "204/404" : expected.ToString(),
                actual, ok);

            return reply;
        }
        catch (Exception exception)
        {
            Record(
                method, path,
                allowMissing ? "204/404" : expected.ToString(),
                actual, false,
                exception.GetType().Name);

            return reply;
        }
    }

    private void Record(
        string method,
        string path,
        string expected,
        int actual,
        bool ok,
        string? reason = null)
    {
        if (ok) passed++;
        else failed++;

        var suffix = ok
            ? ""
            : " check failed" +
              (reason is null ? "" : " (" + reason + ")");

        Console.WriteLine(
            $"{(ok ? "PASS" : "FAIL")} {method} {path} " +
            $"expected={expected} actual={actual}{suffix}");
    }

    private static bool ValidLogin(Reply reply, string role) =>
        reply.Text("role") == role &&
        !string.IsNullOrWhiteSpace(reply.Text("token")) &&
        DateTimeOffset.TryParse(
            reply.Text("expiresAtUtc"), out var expiry) &&
        expiry.Offset == TimeSpan.Zero &&
        expiry > DateTimeOffset.UtcNow;

    private static bool OneItem(Reply reply, int id) =>
        reply.Int("page") == 1 &&
        reply.Int("pageSize") == 10 &&
        reply.Int("totalCount") == 1 &&
        reply.Json.GetProperty("items").GetArrayLength() == 1 &&
        reply.Json.GetProperty("items")[0]
            .GetProperty("id").GetInt32() == id;

    private static bool TranscriptCourse(
        Reply reply,
        string code,
        int credits,
        int mark,
        int passMark) =>
        reply.Json.GetProperty("courses").EnumerateArray().Any(c =>
            c.GetProperty("code").GetString() == code &&
            c.GetProperty("creditHours").GetInt32() == credits &&
            c.GetProperty("mark").GetInt32() == mark &&
            c.GetProperty("passed").GetBoolean() == (mark >= passMark));

    private sealed record Reply(
        int Status,
        JsonElement Json,
        string? Location)
    {
        public int Int(string name) =>
            Json.GetProperty(name).GetInt32();

        public decimal Decimal(string name) =>
            Json.GetProperty(name).GetDecimal();

        public string Text(string name) =>
            Json.GetProperty(name).GetString() ?? string.Empty;

        public bool IsNull(string name) =>
            Json.GetProperty(name).ValueKind == JsonValueKind.Null;
    }

    public void Dispose() => client.Dispose();
}
