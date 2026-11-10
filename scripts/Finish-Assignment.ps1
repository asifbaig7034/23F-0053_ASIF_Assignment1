$ErrorActionPreference = "Stop"
$marksRoot = Split-Path $PSScriptRoot -Parent
Set-Location $marksRoot
$marksEvidence = Join-Path $marksRoot "docs\screenshots"
$marksLogs = Join-Path $marksRoot "docs\verification-logs"
New-Item -ItemType Directory -Path $marksEvidence,$marksLogs -Force | Out-Null
$marksSavedEnvironment = @{}
foreach ($key in @("ConnectionStrings__Database", "SMOKE_TEST", "PGPASSFILE", "PGPASSWORD")) {
    $marksSavedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
}
$marksPassFile = $null
$marksBrowserProfile = $null
$marksTemporaryPdf = $null
$marksProof = [ordered]@{ status = "incomplete"; sourceCommit = ""; freshDatabase = "" }

function Invoke-MarksCommand {
    param([string]$Executable, [string[]]$Arguments, [string]$Log = "")
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        if ($Log) {
            & $Executable @Arguments 2>&1 | Tee-Object -FilePath $Log | ForEach-Object { Write-Host ([string]$_) }
        } else {
            & $Executable @Arguments 2>&1 | ForEach-Object { Write-Host ([string]$_) }
        }
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldPreference }
    if ($code -ne 0) { throw "$Executable failed with exit code $code. See the output above." }
}

function Get-MarksConnectionValue {
    param([string[]]$Names, [string]$Fallback = "")
    foreach ($name in $Names) {
        if ($marksConnection.ContainsKey($name)) { return [string]$marksConnection[$name] }
    }
    return $Fallback
}

function ConvertTo-MarksPassLine {
    param([string]$User, [string]$Password, [string]$Database = "*")
    $fields = @($marksDbHost, $marksDbPort, $Database, $User, $Password)
    return (($fields | ForEach-Object { ([string]$_).Replace('\','\\').Replace(':','\:') }) -join ':')
}

function Invoke-MarksSql {
    param([string]$Database, [string]$Sql)
    $arguments = @("--host=$marksDbHost", "--port=$marksDbPort", "--username=$marksDbUser",
        "--dbname=$Database", "--no-password", "-X", "-A", "-t", "-F", "|", "--set=ON_ERROR_STOP=1", "--command=$Sql")
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $answer = @(& $marksPsql @arguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldPreference }
    if ($code -ne 0) { throw "Database verification query failed." }
    return (($answer | ForEach-Object { [string]$_ }) -join "`n").Trim()
}

function Restore-MarksEnvironment {
    foreach ($key in $marksSavedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $marksSavedEnvironment[$key], "Process")
    }
}

function Save-MarksWindow {
    param([IntPtr]$Handle, [string]$Path)
    if ($Handle -eq [IntPtr]::Zero) { throw "The evidence window could not be found." }
    $original = New-Object 'MarksFinishCapture+Rect'
    if (-not [MarksFinishCapture]::GetWindowRect($Handle, [ref]$original)) { throw "Cannot read evidence window." }
    try {
        [void][MarksFinishCapture]::ShowWindow($Handle, 9)
        $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $width = [Math]::Min(1300, $area.Width)
        $height = [Math]::Min(960, $area.Height)
        [void][MarksFinishCapture]::MoveWindow($Handle, $area.Left, $area.Top, $width, $height, $true)
        [void][MarksFinishCapture]::SetForegroundWindow($Handle)
        Start-Sleep -Milliseconds 700
        $rect = New-Object 'MarksFinishCapture+Rect'
        if (-not [MarksFinishCapture]::GetWindowRect($Handle, [ref]$rect)) { throw "Cannot capture evidence window." }
        $bitmap = [System.Drawing.Bitmap]::new(($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top))
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    } finally {
        [void][MarksFinishCapture]::MoveWindow($Handle, $original.Left, $original.Top,
            ($original.Right-$original.Left), ($original.Bottom-$original.Top), $true)
    }
}

try {
    Write-Host "Marks API final verification - existing application data will be retained."
    Get-Command git,dotnet -CommandType Application -ErrorAction Stop | Out-Null
    $probe = [System.Net.Sockets.TcpClient]::new()
    $portBusy = $false
    try { $probe.Connect("localhost",7134); $portBusy = $true } catch { } finally { $probe.Dispose() }
    if ($portBusy) { throw "Stop the running API with Ctrl+C, then run this script again." }

    $project = Join-Path $marksRoot "src\MarksApi\MarksApi.csproj"
    if (-not (Test-Path $project)) { throw "Extract the finish pack into the existing assignment repository." }
    $envFile = Get-Content (Join-Path $marksRoot "postman\Local.postman_environment.json") -Raw | ConvertFrom-Json
    foreach ($value in $envFile.values) {
        if ($value.key -in @("token","studentToken") -and $value.value) { throw "The environment export contains a token. Use the blank-token file from the finish pack." }
    }
    $connectionText = $marksSavedEnvironment["ConnectionStrings__Database"]
    if ([string]::IsNullOrWhiteSpace($connectionText)) {
        $lines = @(dotnet user-secrets list --project $project | Where-Object { $_ -like "ConnectionStrings:Database = *" })
        if ($LASTEXITCODE -ne 0 -or $lines.Count -ne 1) { throw "Saved application database settings were not found." }
        $connectionText = ($lines[0] -split " = ",2)[1]
    }
    Add-Type -AssemblyName System.Data
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    if (-not ("MarksFinishCapture" -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MarksFinishCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int z,bool repaint);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
    }
    [void][MarksFinishCapture]::SetProcessDPIAware()
    $marksConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $marksConnection.psbase.ConnectionString = $connectionText
    $marksDbHost = Get-MarksConnectionValue -Names "Host","Server" -Fallback "localhost"
    $marksDbPort = Get-MarksConnectionValue -Names "Port" -Fallback "5432"
    $marksDbName = Get-MarksConnectionValue -Names "Database"
    $marksDbUser = Get-MarksConnectionValue -Names "Username","User ID","UserId"
    $dbPassword = Get-MarksConnectionValue -Names "Password","Pwd"
    if (-not $dbPassword -or -not $marksDbUser -or -not $marksDbName) { throw "Saved database settings are incomplete." }
    $pgDump = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\pg_dump.exe" |
        Sort-Object { [version]($_.Directory.Parent.Name + ".0") } -Descending | Select-Object -First 1
    if (-not $pgDump) { throw "PostgreSQL command-line tools were not found." }
    $marksPsql = Join-Path $pgDump.DirectoryName "psql.exe"
    $createdb = Join-Path $pgDump.DirectoryName "createdb.exe"
    if (-not (Test-Path $marksPsql) -or -not (Test-Path $createdb)) { throw "psql.exe or createdb.exe is missing from the PostgreSQL bin folder." }
    $marksPassFile = [System.IO.Path]::GetTempFileName()
    $passLines = @(ConvertTo-MarksPassLine -User $marksDbUser -Password $dbPassword)
    [IO.File]::WriteAllLines($marksPassFile, [string[]]$passLines, [Text.UTF8Encoding]::new($false))
    [Environment]::SetEnvironmentVariable("PGPASSWORD", $null, "Process")
    [Environment]::SetEnvironmentVariable("PGPASSFILE", $marksPassFile, "Process")

    # Stage only assignment paths. Existing secrets remain outside this repository.
    foreach ($oldName in @("Marks API.postman_collection.json", "MarksApi_FIXED.postman_collection.json")) {
        $oldPath = Join-Path $marksRoot ("postman\" + $oldName)
        if (Test-Path $oldPath) {
            $backup = Join-Path (Split-Path $marksRoot -Parent) ("MarksApi_export_backup_" + (Get-Date -Format "yyyyMMddHHmmss"))
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            Move-Item -LiteralPath $oldPath -Destination $backup
        }
    }
    Invoke-MarksCommand -Executable "git" -Arguments @("add", "README.md", "appsettings.Example.json", "db/schema.sql", "docs", "postman", "scripts", "src/MarksApi")
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) {
        Invoke-MarksCommand -Executable "git" -Arguments @("commit", "-m", "Complete API documentation and submission package")
    } elseif ($LASTEXITCODE -ne 0) { throw "Could not inspect staged changes." }
    Invoke-MarksCommand -Executable "git" -Arguments @("push")
    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Cannot read the committed source revision." }
    $remote = (& git remote get-url origin).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $remote) { throw "The Git origin remote was not found." }
    $verifyRoot = Join-Path (Split-Path $marksRoot -Parent) ((Split-Path $marksRoot -Leaf) + "_verify_" + (Get-Date -Format "yyyyMMddHHmmss"))
    Invoke-MarksCommand -Executable "git" -Arguments @("clone", $remote, $verifyRoot) -Log (Join-Path $marksLogs "clone.log")
    $verifyApi = Join-Path $verifyRoot "src\MarksApi"

    $freshDb = "marks_verify_" + (Get-Date -Format "yyyyMMddHHmmss") + "_" + [Guid]::NewGuid().ToString("N").Substring(0,4)
    $canCreate = Invoke-MarksSql -Database $marksDbName -Sql "SELECT (rolcreatedb OR rolsuper)::int FROM pg_roles WHERE rolname=current_user;"
    $createUser = $marksDbUser
    $maintenanceDb = $marksDbName
    if ($canCreate -ne "1") {
        $createUser = Read-Host "PostgreSQL administrator username (Enter for postgres)"
        if ([string]::IsNullOrWhiteSpace($createUser)) { $createUser = "postgres" }
        $adminPassword = Read-Host "PostgreSQL administrator database password" -AsSecureString
        try {
            $adminText = [System.Net.NetworkCredential]::new("",$adminPassword).Password
            $passLines += ConvertTo-MarksPassLine -User $createUser -Password $adminText -Database "postgres"
            [IO.File]::WriteAllLines($marksPassFile,[string[]]$passLines,[Text.UTF8Encoding]::new($false))
        } finally { $adminPassword.Dispose(); $adminText = $null }
        $maintenanceDb = "postgres"
    }
    Invoke-MarksCommand -Executable $createdb -Arguments @("--host=$marksDbHost", "--port=$marksDbPort",
        "--username=$createUser", "--maintenance-db=$maintenanceDb", "--owner=$marksDbUser", "--template=template0", "--no-password", $freshDb)
    $marksProof.sourceCommit = $sourceCommit
    $marksProof.freshDatabase = $freshDb
    $marksConnection["Database"] = $freshDb
    [Environment]::SetEnvironmentVariable("ConnectionStrings__Database", $marksConnection.ToString(), "Process")
    [Environment]::SetEnvironmentVariable("SMOKE_TEST", "false", "Process")

    Push-Location $verifyApi
    try {
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("restore") -Log (Join-Path $marksLogs "restore.log")
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("build", "--no-restore") -Log (Join-Path $marksLogs "build.log")
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("ef", "database", "update", "--", "--environment", "Development") -Log (Join-Path $marksLogs "migrate.log")
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("run", "--no-build", "--launch-profile", "https", "--", "--SeedData", "true") -Log (Join-Path $marksLogs "seed.log")
        $countsSql = "SELECT (SELECT count(*) FROM students),(SELECT count(*) FROM courses),(SELECT count(*) FROM enrollments),(SELECT count(*) FROM users);"
        $initialCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
        if ($initialCounts -ne "2|3|6|3") { throw "Unexpected fresh seed counts: $initialCounts" }
        $smokeResults = @()
        foreach ($run in @(1,2)) {
            $log = Join-Path $marksLogs ("smoke-" + $run + ".log")
            Invoke-MarksCommand -Executable "dotnet" -Arguments @("run", "--no-build", "--launch-profile", "https", "--", "--smoke-test") -Log $log
            $text = Get-Content $log -Raw
            $summary = [regex]::Match($text, "SMOKE SUMMARY: (\d+) passed, (\d+) failed")
            if (-not $summary.Success -or [int]$summary.Groups[1].Value -lt 107 -or $summary.Groups[2].Value -ne "0") { throw "Smoke test summary is not a passing result." }
            $smokeResults += @{ run=$run; passed=[int]$summary.Groups[1].Value; failed=0; exitCode=0 }
        }
        $afterSmokeCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
        if ($afterSmokeCounts -ne "2|3|6|3") { throw "Smoke-test cleanup did not restore the seed row counts." }
        $marksProof["buildExitCode"] = 0
        $marksProof["seedCounts"] = $initialCounts
        $marksProof["smokeRuns"] = $smokeResults
        Clear-Host
        Write-Host "ACTUAL SMOKE OUTPUT - FRESH DATABASE CLEAN CLONE"
        Get-Content (Join-Path $marksLogs "smoke-2.log") -Tail 12 | ForEach-Object { Write-Host $_ }
        Write-Host "Both smoke runs exited 0. Seed row counts after cleanup: 2|3|6|3"
        [void](Read-Host "Press Enter here to save the smoke-test console screenshot")
        Save-MarksWindow -Handle ([MarksFinishCapture]::GetForegroundWindow()) -Path (Join-Path $marksEvidence "smoke-tests.png")

        $sequenceBefore = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
        $shellExe = Join-Path $PSHOME "powershell.exe"
        Start-Process -FilePath $shellExe -WorkingDirectory $verifyApi -ArgumentList @("-NoExit", "-Command", 'dotnet run --no-build --launch-profile https') | Out-Null
        $healthy = $false
        for ($attempt=0; $attempt -lt 30; $attempt++) {
            try {
                $health = Invoke-RestMethod -UseBasicParsing -Uri "https://localhost:7134/health" -TimeoutSec 3
                if ($health.status -eq "Healthy") { $healthy=$true; break }
            } catch { }
            Start-Sleep -Milliseconds 500
        }
        if (-not $healthy) { throw "The clean-copy API did not become healthy. Inspect its new terminal." }
        Write-Host "ONE FINAL POSTMAN ACTION: rerun the complete Marks API collection using Local."
        Write-Host "Expected: 59 requests, 118 passed, 0 failed, 0 errors. Keep the result visible."
        $confirmed = Read-Host "When that fresh run finishes, type its passed-test count here (expected 118)"
        if ($confirmed -ne "118") { throw "Fresh Postman run has not been confirmed as 118 passed." }
        $finalCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
        $sequenceAfter = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
        if ($finalCounts -ne "2|3|6|3" -or $sequenceAfter -le $sequenceBefore) {
            throw "The fresh database does not show the expected completed Postman fixture lifecycle. Rerun the full collection against this API."
        }
        $postmanWindow = Get-Process -Name Postman -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if (-not $postmanWindow) { throw "Open the Postman Desktop run-results window before saving evidence." }
        Save-MarksWindow -Handle $postmanWindow.MainWindowHandle -Path (Join-Path $marksEvidence "postman-runner.png")
        $marksProof["postman"] = @{ requests=59; passed=118; failed=0; errors=0; source="operator confirmation, captured window and verified fresh-database fixture lifecycle" }
        $marksProof["finalRowCounts"] = $finalCounts
        $marksProof["studentSequenceAdvanced"] = $true
        $marksProof["verifiedAtUtc"] = [DateTime]::UtcNow.ToString("o")
        $marksProof.status = "passed"
    } finally { Pop-Location }

    Restore-MarksEnvironment
    $proofFile = Join-Path $marksRoot "docs\verification-results.json"
    $marksProof | ConvertTo-Json -Depth 8 | Set-Content $proofFile -Encoding UTF8
    $template = Get-Content (Join-Path $marksRoot "docs\report.template.html") -Raw
    $template = $template.Replace("__VERIFY_STATUS__", "PASSED - fresh database, clean GitHub clone, two smoke runs and full Postman run.")
    $template = $template.Replace('class="verification pending"', 'class="verification complete"')
    $template = $template.Replace("__SMOKE_EVIDENCE__", '<div class="crop smoke"><img src="screenshots/smoke-tests.png"></div>')
    $template | Set-Content (Join-Path $marksRoot "docs\report.html") -Encoding UTF8
    $browser = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $browser) { throw "Open docs/report.html in a browser and print to PDF as docs/report.pdf; then commit the completed evidence." }
    $marksBrowserProfile = Join-Path ([IO.Path]::GetTempPath()) ("MarksPdf-" + [Guid]::NewGuid().ToString("N"))
    $pdfPath = Join-Path $marksRoot "docs\report.pdf"
    $marksTemporaryPdf = Join-Path $marksRoot ("docs\report-render-" + [Guid]::NewGuid().ToString("N") + ".pdf")
    $htmlUri = ([Uri](Join-Path $marksRoot "docs\report.html")).AbsoluteUri
    $printArguments = @("--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
        "--disable-background-networking", "--no-pdf-header-footer", ('--user-data-dir="' + $marksBrowserProfile + '"'),
        ('--print-to-pdf="' + $marksTemporaryPdf + '"'), $htmlUri)
    $printProcess = Start-Process -FilePath $browser -ArgumentList $printArguments -Wait -PassThru
    if ($printProcess.ExitCode -ne 0 -or -not (Test-Path $marksTemporaryPdf) -or (Get-Item $marksTemporaryPdf).Length -lt 1000) {
        throw "The final PDF could not be generated. Open docs/report.html and print it as docs/report.pdf."
    }
    Move-Item -LiteralPath $marksTemporaryPdf -Destination $pdfPath -Force
    Set-Location $marksRoot
    Invoke-MarksCommand -Executable "git" -Arguments @("add", "docs", "README.md", "appsettings.Example.json", "db/schema.sql", "postman", "scripts")
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) { Invoke-MarksCommand -Executable "git" -Arguments @("commit", "-m", "Record fresh database clean-clone verification and final report") }
    elseif ($LASTEXITCODE -ne 0) { throw "Could not inspect final staged changes." }
    Invoke-MarksCommand -Executable "git" -Arguments @("push")
    Write-Host "FINAL READY: docs\report.pdf generated; evidence and project pushed to GitHub." -ForegroundColor Green
    Write-Host "Review the PDF, share the private repository with your instructor, and submit the PDF."
    Start-Process $pdfPath | Out-Null
    exit 0
}
catch {
    $marksProof.status = "incomplete"
    $marksProof["failureType"] = $_.Exception.GetType().Name
    $marksProof | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $marksRoot "docs\verification-results.json") -Encoding UTF8
    Write-Host ("STOPPED: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host "Existing application data was retained. Send the STOPPED message if help is needed."
    exit 1
}
finally {
    Restore-MarksEnvironment
    if ($marksPassFile) { Remove-Item -LiteralPath $marksPassFile -Force -ErrorAction SilentlyContinue }
    if ($marksBrowserProfile) { Remove-Item -LiteralPath $marksBrowserProfile -Recurse -Force -ErrorAction SilentlyContinue }
    if ($marksTemporaryPdf) { Remove-Item -LiteralPath $marksTemporaryPdf -Force -ErrorAction SilentlyContinue }
    if ($marksConnection) { $marksConnection.Clear() }
    $connectionText=$null; $dbPassword=$null; $passLines=$null; $lines=$null
}
