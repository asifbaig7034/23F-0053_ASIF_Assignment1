param(
    [string]$Database = "marks_db",
    [string]$DatabaseUser = "marks_user",
    [string]$DatabaseHost = "localhost",
    [int]$DatabasePort = 5432
)
$ErrorActionPreference = "Stop"
$marksRoot = Split-Path $PSScriptRoot -Parent
$marksProject = Join-Path $marksRoot "src\MarksApi\MarksApi.csproj"
Add-Type -AssemblyName System.Data
$marksDbPassword = Read-Host "Database password for $DatabaseUser" -AsSecureString
$marksTeacherPassword = Read-Host "Teacher demo password (see README)" -AsSecureString
$marksStudentPassword = Read-Host "Student demo password (see README)" -AsSecureString
$marksRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$marksBytes = New-Object byte[] 32
$marksOldEncoding = $OutputEncoding
try {
    $marksBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $marksBuilder["Host"] = $DatabaseHost
    $marksBuilder["Port"] = $DatabasePort
    $marksBuilder["Database"] = $Database
    $marksBuilder["Username"] = $DatabaseUser
    $marksBuilder["Password"] = [System.Net.NetworkCredential]::new("", $marksDbPassword).Password
    $marksRng.GetBytes($marksBytes)
    $marksSettings = @{
        "ConnectionStrings:Database" = $marksBuilder.ToString()
        "Jwt:Key" = [Convert]::ToBase64String($marksBytes)
        "Seed:Teacher:Username" = "teacher1"
        "Seed:Teacher:Password" = [System.Net.NetworkCredential]::new("", $marksTeacherPassword).Password
        "Seed:Student1:Username" = "student1"
        "Seed:Student1:Password" = [System.Net.NetworkCredential]::new("", $marksStudentPassword).Password
        "Seed:Student2:Username" = "student2"
        "Seed:Student2:Password" = [System.Net.NetworkCredential]::new("", $marksStudentPassword).Password
        "SmokeTest:TeacherUsername" = "teacher1"
        "SmokeTest:TeacherPassword" = [System.Net.NetworkCredential]::new("", $marksTeacherPassword).Password
        "SmokeTest:StudentUsername" = "student1"
        "SmokeTest:StudentPassword" = [System.Net.NetworkCredential]::new("", $marksStudentPassword).Password
    }
    $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $marksSettings | ConvertTo-Json | dotnet user-secrets set --project $marksProject
    if ($LASTEXITCODE -ne 0) { throw "Could not save project user-secrets." }
    Write-Host "Development settings saved. Next: migrations, seed and build."
}
finally {
    $OutputEncoding = $marksOldEncoding
    $marksRng.Dispose()
    $marksDbPassword.Dispose()
    $marksTeacherPassword.Dispose()
    $marksStudentPassword.Dispose()
    [Array]::Clear($marksBytes, 0, $marksBytes.Length)
    if ($marksBuilder) { $marksBuilder.Clear() }
    $marksSettings = $null
}
