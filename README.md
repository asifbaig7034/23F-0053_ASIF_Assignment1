\## Development demo data



These accounts are for local development only. Passwords are stored

as hashes using ASP.NET Core PasswordHasher.



| Role | Username | Demo password |

|---|---|---|

| Teacher | teacher1 | TeacherDemo123! |

| Student | student1 | StudentDemo123! |

| Student | student2 | StudentDemo123! |



Run the following commands from src/MarksApi:



```powershell

dotnet user-secrets set "Seed:Teacher:Username" "teacher1"

dotnet user-secrets set "Seed:Teacher:Password" "TeacherDemo123!"

dotnet user-secrets set "Seed:Student1:Username" "student1"

dotnet user-secrets set "Seed:Student1:Password" "StudentDemo123!"

dotnet user-secrets set "Seed:Student2:Username" "student2"

dotnet user-secrets set "Seed:Student2:Password" "StudentDemo123!"



dotnet run -- --environment Development --SeedData true

```



Run the seed command after applying migrations to a fresh database.

It creates 2 students, 3 courses, 6 enrollments and 3 users, then exits.

When users already exist, initialization is skipped.


## Startup smoke tests

Configure these user-secrets from src/MarksApi using the development
account credentials documented in this README:

    dotnet user-secrets set "SmokeTest:TeacherUsername" "teacher1"
    dotnet user-secrets set "SmokeTest:TeacherPassword" "<teacher-demo-password>"
    dotnet user-secrets set "SmokeTest:StudentUsername" "student1"
    dotnet user-secrets set "SmokeTest:StudentPassword" "<student-demo-password>"

Stop any running instance of the API before starting the tests.

Run from the repository root:

    cd src/MarksApi
    dotnet build
    dotnet run --no-build --launch-profile https -- --smoke-test

The module starts the real API and tests it through HttpClient against
PostgreSQL. It checks successful requests, authorization, validation,
conflicts, missing records, and report calculations.

Temporary records use a SMOKE prefix and are cleaned up after the run.
Every check prints its expected and actual status. Exit code 0 means
all checks passed; a non-zero exit code means a failure occurred.

Run the command twice to verify repeatability and cleanup.

Normal startup does not run the tests:

    dotnet run --no-build --launch-profile https

HTTPS base URL: https://localhost:7134
Swagger UI: https://localhost:7134/swagger/index.html
