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

