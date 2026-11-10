# Viva notes - Marks API

1. **Why PostgreSQL constraints too?** Concurrent requests can both pass an application pre-check. A unique index decides atomically; 23505 becomes 409.
2. **Why 400 vs 409?** 400 is invalid input. 409 is valid input conflicting with current database state.
3. **Why 401 vs 403?** 401 needs valid authentication. 403 means an authenticated caller lacks permission.
4. **Null vs zero mark?** Null is ungraded and excluded from averages. Zero is graded and included.
5. **Weighted average?** Sum mark times credits, divided by credits of graded courses. Ali: (80*3+60*2)/5=72.
6. **Own-data check?** Student role plus studentId claim must match the requested student. sub is the user account ID.
7. **Why DTOs/AsNoTracking?** Control the API contract and avoid returning entities or tracking read-only data.
8. **Why RESTRICT deletion?** Preserve enrollments and linked accounts; report the conflict as 409.
9. **What does smoke test prove?** Real HTTP requests use the real database, exercise errors, clean fixtures and return a useful exit code.
10. **What does the health endpoint prove?** API and database connectivity. It does not replace full business tests.
11. **Where are secrets?** Development user-secrets, deployment environment variables. PasswordHasher hashes account passwords.
12. **How is the project reproduced?** Clone, configure secrets, migrate, seed, build, run, then test. Point out the fresh-clone verification record.

Be ready to show a new student, enroll the student in a new course, assign a mark,
explain the transcript calculation, and delete the temporary enrollment before
deleting its student/course. Mark edits currently use the stored current value;
optimistic concurrency/versioning is not claimed as an implemented bonus.
