# Student Marks Management API

**Asif Baig - 23F-0053 - Advanced Programming, Assignment 1**

Repository: https://github.com/asifbaig7034/23F-0053_ASIF_Assignment1

ASP.NET Core API for students, courses, enrollments, marks and reports. Data is
stored in PostgreSQL through EF Core migrations. JWT roles enforce Teacher
permissions and Student access to their own record and transcript.

## Requirements

- .NET 10 SDK (development environment used SDK 10.0.403).
- PostgreSQL 17 with command-line tools; the exported schema records version 17.11.
- Git, Postman Desktop and Windows PowerShell 5.1 or later.
- EF Core CLI 10.0.14. Install it if it is not already present:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
```

## Run from a clean copy

### 1. Clone and restore

```powershell
git clone https://github.com/asifbaig7034/23F-0053_ASIF_Assignment1.git
cd 23F-0053_ASIF_Assignment1
dotnet restore .\src\MarksApi\MarksApi.csproj
```

The repository is private. The GitHub account performing the clone needs access.

### 2. Create the database

On a new PostgreSQL installation, open psql as the administrator:

```powershell
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h localhost -U postgres -d postgres -W
```

Run these SQL/psql commands. `\password` prompts for the application user's
password without placing it in SQL command text:

```sql
CREATE USER marks_user;
\password marks_user
CREATE DATABASE marks_db OWNER marks_user;
\q
```

Use the installation's PostgreSQL version in the executable path. For an
existing installation, reuse the application role and create an empty database
owned by that role. The application connects as `marks_user`.

### 3. Configure secrets and HTTPS

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Configure-Development.ps1
dotnet dev-certs https --trust
```

The configuration script asks for the database password and the development
Teacher/Student passwords. For the supplied Postman collection, use the demo
passwords in the table below. It creates a random JWT signing key and saves
connection, seeding and smoke-test settings to project user-secrets.

`appsettings.Example.json` documents every setting with fake secret values.
Non-secret runtime settings are in `src/MarksApi/appsettings.json`. User-secrets
are loaded in Development; deployment secrets can use environment variables,
such as `ConnectionStrings__Database` and `Jwt__Key`.

### 4. Apply migrations, seed and build

```powershell
cd .\src\MarksApi
dotnet ef database update -- --environment Development
dotnet run --launch-profile https -- --SeedData true
dotnet build
```

Migrations create the database tables. `db/schema.sql` is a schema-only export
for inspection; use migrations to initialize the application.

The explicit Development seed command creates two students, three courses, six
enrollments and three users, then exits. Passwords are stored as ASP.NET Core
PasswordHasher hashes. Seeding is skipped when users already exist. Normal
startup does not seed data or apply migrations automatically.

### 5. Run the API

```powershell
dotnet run --no-build --launch-profile https
```

Keep that terminal running while using Postman.

- HTTPS API: https://localhost:7134
- Health: https://localhost:7134/health
- Interactive Swagger UI (Development): https://localhost:7134/swagger/index.html
- HTTP: http://localhost:5134 redirects to HTTPS.
- Browser CORS origins are read from `Cors:AllowedOrigins`; the example allows
  `http://localhost:3000`.

## Development demo accounts

These accounts and passwords are for local development only.

| Role | Username | Demo password | Linked student |
|---|---|---|---|
| Teacher | teacher1 | TeacherDemo123! | None |
| Student | student1 | StudentDemo123! | Ali Raza |
| Student | student2 | StudentDemo123! | Sara Khan |

## Endpoints

All API bodies use JSON and camelCase DTO properties. Protected requests use
`Authorization: Bearer <token>`.

| Method | Path | Access | Success |
|---|---|---|---|
| POST | /api/auth/login | Anyone | 200 |
| GET | /api/students | Teacher | 200 |
| POST | /api/students | Teacher | 201 + Location |
| GET | /api/students/{id} | Teacher or that Student | 200 |
| PUT | /api/students/{id} | Teacher | 200 |
| DELETE | /api/students/{id} | Teacher | 204 |
| GET | /api/courses | Teacher or Student | 200 |
| POST | /api/courses | Teacher | 201 + Location |
| GET | /api/courses/{id} | Teacher or Student | 200 |
| PUT | /api/courses/{id} | Teacher | 200 |
| DELETE | /api/courses/{id} | Teacher | 204 |
| POST | /api/courses/{courseId}/enrollments | Teacher | 201 + Location |
| GET | /api/courses/{courseId}/enrollments | Teacher | 200 |
| GET | /api/enrollments/{id} | Teacher | 200 |
| PUT | /api/enrollments/{id}/mark | Teacher | 200 |
| DELETE | /api/enrollments/{id} | Teacher | 204 |
| GET | /api/courses/{courseId}/stats | Teacher | 200 |
| GET | /api/students/{id}/transcript | Teacher or that Student | 200 |
| GET | /health | Anyone | 200; 503 when unhealthy |

The extra enrollment lookup makes the enrollment create Location resolvable.
Errors use Problem Details with `status`, `title` and `detail`; validation
problems also include `errors`. Common error codes are 400 (invalid input),
401 (missing/invalid authentication), 403 (forbidden), 404 (missing resource)
and 409 (duplicate or restricted deletion).

### Paging, search and sort

List endpoints accept `page` (default 1), `pageSize` (default 10, maximum 50),
`search` and `sort`. Invalid paging/sort values return 400. Results contain
`items`, `page`, `pageSize` and `totalCount`. Prefix a sort field with `-` for
descending order; secondary ID sorting keeps results stable.

| List | Sort fields |
|---|---|
| Students | id, registrationNo, fullName, email |
| Courses | id, code, title, creditHours |
| Course enrollments | id, studentId, fullName, mark, enrolledAtUtc |

## Design decisions and calculations

- Endpoints handle HTTP, DTO validation and authorization; services behind
  interfaces implement operations, with scoped AppDbContext data access.
- Read queries use AsNoTracking and DTO projections. Database calls are async
  and receive cancellation tokens.
- PostgreSQL unique indexes handle duplicates atomically. Unique violation
  23505 becomes 409, including concurrent duplicate enrollments.
- Restricted foreign keys prevent deleting enrolled students/courses and
  linked student accounts. Deletion conflicts return 409; missing enrollment
  parents return 404.
- Emails are normalized to lower case; course codes to upper case. Required
  lengths, credit hours 1-4 and nullable integer marks 0-100 are validated.
- UTC timestamps use PostgreSQL timestamptz. A null mark means ungraded;
  zero is a graded mark.
- Course statistics use graded marks only. Average/highest/lowest are null
  when there are no grades. A passing mark uses `Grading:PassMark` (50).
- Weighted average is `sum(mark * creditHours) / sum(creditHours)` over
  graded courses only; an empty/ungraded transcript has a null average.
- A Student's `studentId` claim governs own-record access; the JWT `sub`
  identifies the user account. Passwords/JWTs are not logged.

Seed checks: CS401 has marks 80 and 70, so its average is 75. Ali's graded
courses are 80 at 3 credits and 60 at 2 credits: `(80*3 + 60*2)/(3+2) = 72`.

## Postman collection

Import `postman/MarksApi.postman_collection.json` and
`postman/Local.postman_environment.json`, then select Local. Its baseUrl is
`https://localhost:7134`. The exported `token` and `studentToken` are blank.

Run all **59 requests / 118 tests** in saved folder order:

1. Auth
2. Students
3. Courses
4. Enrollments and Marks
5. Reports
6. Health
7. Negative Tests

Teacher requests inherit collection Bearer `{{token}}`; Student checks use
`{{studentToken}}`. Login and health are No Auth; the intentional no-token
negative test is also No Auth. Login scripts save tokens automatically.

Create scripts generate unique temporary values and save studentId, courseId
and enrollmentId in collection variables. Cleanup requests remove their test
records. There are 32 negative cases; every request has two tests. The expected
full-run result is **118 passed, 0 failed, 0 errors**. For final evidence, run
once against freshly migrated and seeded data.

## Startup smoke tests

The configuration script saves SmokeTest:TeacherUsername,
SmokeTest:TeacherPassword, SmokeTest:StudentUsername and
SmokeTest:StudentPassword to user-secrets. Stop the normal API before testing:

```powershell
# From src/MarksApi
dotnet run --no-build --launch-profile https -- --smoke-test
Write-Host "Exit code: $LASTEXITCODE"
```

The module starts the real API, calls it with HttpClient against PostgreSQL,
checks success/error responses and report edge cases, cleans up SMOKE-prefixed
fixtures, prints expected/actual status for every check, then exits. It can
also be requested with `SMOKE_TEST=true` in the process environment.

Run it twice. Recorded implementation results were **107 passed, 0 failed**
and exit code **0**. Normal startup does not run smoke tests. The test mode
requires Development and refuses to combine seeding and smoke execution.

## Final clean-clone verification

From the project root, stop the API and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Finish-Assignment.ps1
```

This script commits/pushes the prepared project files, clones the repository
into a new sibling directory, creates a separate verification database, applies
migrations, seeds it and runs smoke tests twice. It asks for the PostgreSQL
administrator password only when the application role cannot create a database.
Existing application data and stored user-secrets are retained.

It then starts that clean copy for one fresh Postman run. Follow the console
prompt to run the collection and record the result. Actual console/Postman
screenshots, logs and `docs/verification-results.json` are saved. Edge/Chrome
prints the supplied report template to `docs/report.pdf`, after which evidence
is committed and pushed. If a step fails, the script stops with a non-zero exit.

The separate verification database is retained for inspection. Its name and the
clean checkout commit are recorded in verification-results.json. Only use a
finished verification record as evidence of the fresh-database clean-clone check.

## Repository layout

```text
src/MarksApi/             Application, migrations, services and smoke tests
postman/                 Portable collection and token-free environment
db/schema.sql            Actual PostgreSQL schema-only export
docs/erd.png             ER diagram based on schema.sql
docs/report.pdf          Five-page report and evidence
docs/report.html         Printable report template
docs/screenshots/        Individual HTTP and runner/smoke evidence
scripts/                 Configuration and final verification helpers
appsettings.Example.json Fake values documenting all settings
```

## Troubleshooting

- Connection refused: start the API and keep its terminal running.
- 401: select Local and rerun Auth login requests to refresh tokens.
- 403 in a negative test is expected; compare its expected status.
- psql not found: use the PostgreSQL bin path shown above.
- Database password failure: use the marks_user database password; it is
  separate from the development API login passwords.
- HTTPS certificate error: trust the development certificate and restart the
  client. Keep HTTPS verification enabled for normal use.
- Missing/bad JSON body: choose raw JSON and application/json in Postman.

## Submission

Review docs/report.pdf, confirm fresh verification passed, push the final
repository, share the private GitHub repository with the instructor, and submit
the PDF using the announced submission channel. The short viva notes are in
docs/viva-notes.md.
