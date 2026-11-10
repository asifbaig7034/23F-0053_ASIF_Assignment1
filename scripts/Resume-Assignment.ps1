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
    Write-Host "Resume Marks API verification from the saved smoke-test results."
    Get-Command git,dotnet -CommandType Application -ErrorAction Stop | Out-Null
    $proofFile = Join-Path $marksRoot "docs\verification-results.json"
    if (-not (Test-Path $proofFile)) { throw "Saved verification results were not found. Run Finish-Assignment.ps1 first." }
    $savedProof = Get-Content -LiteralPath $proofFile -Raw | ConvertFrom-Json
    foreach ($property in $savedProof.PSObject.Properties) { $marksProof[$property.Name] = $property.Value }
    $marksProof.status = "incomplete"
    $freshDb = [string]$marksProof["freshDatabase"]
    $sourceCommit = [string]$marksProof["sourceCommit"]
    if ($freshDb -notmatch '^marks_verify_[0-9]{14}_[a-f0-9]{4}$' -or $sourceCommit -notmatch '^[a-f0-9]{40}$') {
        throw "The saved fresh-database verification record is incomplete."
    }
    $smokeRuns = @($marksProof["smokeRuns"])
    if ($smokeRuns.Count -ne 2) { throw "Two saved passing smoke runs are required to resume." }
    foreach ($run in $smokeRuns) {
        if ($null -eq $run.passed -or $null -eq $run.failed -or $null -eq $run.exitCode -or
            [int]$run.passed -lt 107 -or [int]$run.failed -ne 0 -or [int]$run.exitCode -ne 0) {
            throw "A saved smoke run has not passed. Run the full verification again."
        }
    }
    if ([string]$marksProof["seedCounts"] -ne "2|3|6|3" -or -not (Test-Path (Join-Path $marksEvidence "smoke-tests.png"))) {
        throw "The saved seed counts or actual smoke screenshot are missing."
    }
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


    $marksConnection.psbase.ConnectionString = ([string]$connectionText).TrimEnd(";") + ";Database=" + [string]$freshDb
    [Environment]::SetEnvironmentVariable("ConnectionStrings__Database", $marksConnection.ToString(), "Process")
    [Environment]::SetEnvironmentVariable("SMOKE_TEST", "false", "Process")
    $countsSql = "SELECT (SELECT count(*) FROM students),(SELECT count(*) FROM courses),(SELECT count(*) FROM enrollments),(SELECT count(*) FROM users);"
    $initialCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
    if ($initialCounts -ne "2|3|6|3") { throw "The verification database contains unfinished test fixtures: $initialCounts. Send this message for help." }
    $sequenceBefore = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")

    $verifyRoot = $null
    $candidateRoots = @()
    if ($marksProof["verificationCheckout"]) { $candidateRoots += [string]$marksProof["verificationCheckout"] }
    $candidateRoots += @(Get-ChildItem -LiteralPath (Split-Path $marksRoot -Parent) -Directory -Filter ((Split-Path $marksRoot -Leaf) + "_verify_*") |
        Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName)
    foreach ($candidate in $candidateRoots) {
        if (-not (Test-Path (Join-Path $candidate "src\MarksApi\MarksApi.csproj"))) { continue }
        $revision = @(& git -C $candidate rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and ($revision -join "").Trim() -eq $sourceCommit) { $verifyRoot=$candidate; break }
    }
    if (-not $verifyRoot) { throw "The saved clean checkout could not be found beside the assignment folder." }
    & git -C $verifyRoot diff --quiet HEAD -- src/MarksApi
    if ($LASTEXITCODE -ne 0) { throw "The verified checkout's application source has changed. Run the full verification again." }
    $verifyApi = Join-Path $verifyRoot "src\MarksApi"
    $marksProof["verificationCheckout"] = $verifyRoot
    $marksProof["postmanSequenceBefore"] = $sequenceBefore
    $marksProof | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $proofFile -Encoding UTF8

    $healthy = $false
    try {
        $health = Invoke-RestMethod -UseBasicParsing -Uri "https://localhost:7134/health" -TimeoutSec 3
        $healthy = ($health.status -eq "Healthy")
    } catch { }
    if (-not $healthy) {
        $shellExe = Join-Path $PSHOME "powershell.exe"
        Start-Process -FilePath $shellExe -WorkingDirectory $verifyApi -ArgumentList @("-NoExit", "-Command", 'dotnet run --no-build --launch-profile https') | Out-Null
        for ($attempt=0; $attempt -lt 30; $attempt++) {
            try {
                $health = Invoke-RestMethod -UseBasicParsing -Uri "https://localhost:7134/health" -TimeoutSec 3
                if ($health.status -eq "Healthy") { $healthy=$true; break }
            } catch { }
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $healthy) { throw "The verification API is not healthy. Inspect its terminal." }

    Write-Host "POSTMAN: Local select karein. Marks API collection > ... > Run, ya run results mein Rerun."
    Write-Host "Poori collection chalayein: 59 requests; Passed 118, Failed 0, Errors 0."
    Write-Host "Run results Postman mein khule rakhein; phir isi console mein 118 likhein."
    while ($true) {
        $confirmed = (Read-Host "Fresh run ke 118 tests PASS hone par 118 likhein (Q to stop)").Trim()
        if ($confirmed -eq "118") { break }
        if ($confirmed -eq "Q") { throw "Postman verification is pending. Run Resume-Assignment.ps1 again to continue." }
        Write-Host "Pehle Postman mein complete collection Run/Rerun karein. Result PASS ho to yahan 118 likhein."
    }
    $finalCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
    $sequenceAfter = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
    if ($finalCounts -ne "2|3|6|3" -or $sequenceAfter -le $sequenceBefore) {
        throw "The fresh database does not show the completed Postman fixture lifecycle. Check Local baseUrl is https://localhost:7134 and rerun the complete collection."
    }
    $postmanWindow = Get-Process -Name Postman -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $postmanWindow) { throw "Open the Postman Desktop run-results window before saving evidence." }
    Save-MarksWindow -Handle $postmanWindow.MainWindowHandle -Path (Join-Path $marksEvidence "postman-runner.png")
    $marksProof["postman"] = @{ requests=59; passed=118; failed=0; errors=0; source="operator confirmation, captured window and verified fresh-database fixture lifecycle" }
    $marksProof["finalRowCounts"] = $finalCounts
    $marksProof["studentSequenceAdvanced"] = $true
    $marksProof["verifiedAtUtc"] = [DateTime]::UtcNow.ToString("o")
    if ($marksProof.Contains("failureType")) { $marksProof.Remove("failureType") }
    $marksProof.status = "passed"

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


