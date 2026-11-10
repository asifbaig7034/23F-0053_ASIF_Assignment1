param([string]$ProjectRoot = "C:\Users\pc\23F-0053_ASIF_Assignment1")
$ErrorActionPreference = "Stop"
$marksRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
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

function Save-MarksCheckpoint {
    if ([string]$marksProof["sourceCommit"] -notmatch '^[a-fA-F0-9]{40,64}$' -or
        [string]$marksProof["freshDatabase"] -notmatch '^marks_verify_[0-9]+_[0-9a-fA-F]+$') {
        throw "Cannot save a checkpoint without an identified checkout and verification database."
    }
    $checkpointPath = Join-Path $marksRoot "docs\verification-results.json"
    $checkpointTemporary = $checkpointPath + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    try {
        $checkpointJson = $marksProof | ConvertTo-Json -Depth 8
        [IO.File]::WriteAllText($checkpointTemporary, $checkpointJson, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $checkpointTemporary -Destination $checkpointPath -Force
    } finally { Remove-Item -LiteralPath $checkpointTemporary -Force -ErrorAction SilentlyContinue }
}

function Test-MarksApiPort {
    $probe = [Net.Sockets.TcpClient]::new()
    try { $probe.Connect("localhost",7134); return $true }
    catch { return $false }
    finally { $probe.Dispose() }
}

try {
    Write-Host "Marks API recovery: locate the verification checkout/database and rebuild the saved checkpoint."
    Get-Command git,dotnet -CommandType Application -ErrorAction Stop | Out-Null
    $marksProof = [ordered]@{ schemaVersion=2; status="recovering"; stage="locating"; sourceCommit=""; freshDatabase="" }
    $marksRunStamp = Get-Date -Format "yyyyMMddHHmmss"
    $marksProof["recoveryRun"] = $marksRunStamp
    $marksBackup = Join-Path (Split-Path $marksRoot -Parent) ("MarksApi_verification_backup\" + $marksRunStamp + "_" + [Guid]::NewGuid().ToString("N").Substring(0,4))
    New-Item -ItemType Directory -Path $marksBackup -Force | Out-Null
    foreach ($relative in @("docs\verification-results.json", "docs\screenshots\smoke-tests.png", "docs\screenshots\postman-runner.png")) {
        $previous = Join-Path $marksRoot $relative
        if (Test-Path -LiteralPath $previous) { Copy-Item -LiteralPath $previous -Destination (Join-Path $marksBackup (Split-Path $relative -Leaf)) -Force }
    }
    $installedHelper = Join-Path $marksRoot "scripts\Recover-Assignment.ps1"
    if ([IO.Path]::GetFullPath($PSCommandPath) -ne [IO.Path]::GetFullPath($installedHelper)) {
        Copy-Item -LiteralPath $PSCommandPath -Destination $installedHelper -Force
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



    $countsSql = "SELECT (SELECT count(*) FROM students),(SELECT count(*) FROM courses),(SELECT count(*) FROM enrollments),(SELECT count(*) FROM users);"
    $databaseSql = @'
SELECT d.datname
FROM pg_database d
WHERE d.datname ~ '^marks_verify_[0-9]+_[0-9a-fA-F]+$'
  AND d.datdba = (SELECT oid FROM pg_roles WHERE rolname=current_user)
  AND d.datallowconn AND NOT d.datistemplate
ORDER BY EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.datname=d.datname AND a.usename=current_user) DESC,
         d.datname DESC;
'@
    $databaseText = Invoke-MarksSql -Database $marksDbName -Sql $databaseSql
    $databaseNames = @($databaseText -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^marks_verify_[0-9]+_[0-9a-fA-F]+$' })
    $freshDb = $null
    foreach ($databaseName in $databaseNames) {
        try {
            if ((Invoke-MarksSql -Database $databaseName -Sql $countsSql) -eq "2|3|6|3") { $freshDb=$databaseName; break }
        } catch { }
    }
    $marksNewDatabase = $false
    if (-not $freshDb) {
        Write-Host "No complete verification database was found. Creating a separate fresh verification database."
    $freshDb = "marks_verify_" + (Get-Date -Format "yyyyMMddHHmmss") + "_" + [Guid]::NewGuid().ToString("N").Substring(0,4)
    $canCreate = Invoke-MarksSql -Database $marksDbName -Sql "SELECT (rolcreatedb OR rolsuper)::int FROM pg_roles WHERE rolname=current_user;"
    $createUser = $marksDbUser
    $maintenanceDb = $marksDbName
    if ($canCreate -ne "1") {
        $createUser = "postgres"
        Write-Host "Database administrator: postgres"
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

        $marksNewDatabase = $true
    }
    $marksProof["freshDatabase"] = [string]$freshDb
    $marksProof["databaseCreatedThisRun"] = $marksNewDatabase
    Write-Host ("Verification database: " + $freshDb)
    $marksConnection.psbase.ConnectionString = ([string]$connectionText).TrimEnd(";") + ";Database=" + [string]$freshDb
    [Environment]::SetEnvironmentVariable("ConnectionStrings__Database", $marksConnection.ToString(), "Process")
    [Environment]::SetEnvironmentVariable("SMOKE_TEST", "false", "Process")

    $expectedTreeOutput = @(& git -C $marksRoot rev-parse "HEAD:src/MarksApi")
    if ($LASTEXITCODE -ne 0) { throw "Cannot identify the committed API source in the assignment repository." }
    $expectedTree = ($expectedTreeOutput -join "").Trim()
    if ($expectedTree -notmatch '^[a-fA-F0-9]{40,64}$') { throw "Cannot identify the committed source tree." }
    $verifyRoot = $null
    $checkoutCandidates = @(Get-ChildItem -LiteralPath (Split-Path $marksRoot -Parent) -Directory -Filter ((Split-Path $marksRoot -Leaf) + "_verify_*") |
        Sort-Object Name -Descending | Select-Object -ExpandProperty FullName)
    foreach ($candidate in $checkoutCandidates) {
        if (-not (Test-Path (Join-Path $candidate "src\MarksApi\MarksApi.csproj"))) { continue }
        $treeOutput = @(& git -C $candidate rev-parse "HEAD:src/MarksApi" 2>$null)
        if ($LASTEXITCODE -ne 0 -or ($treeOutput -join "").Trim() -ne $expectedTree) { continue }
        $changedFiles = @(& git -C $candidate status --porcelain --untracked-files=all -- src/MarksApi)
        if ($LASTEXITCODE -eq 0 -and $changedFiles.Count -eq 0) { $verifyRoot=$candidate; break }
    }
    if (-not $verifyRoot) {
        $remoteOutput = @(& git -C $marksRoot remote get-url origin)
        if ($LASTEXITCODE -ne 0) { throw "The origin remote was not found." }
        $remote = ($remoteOutput -join "").Trim()
        $verifyRoot = Join-Path (Split-Path $marksRoot -Parent) ((Split-Path $marksRoot -Leaf) + "_verify_" + $marksRunStamp + "_recovery")
        Invoke-MarksCommand -Executable "git" -Arguments @("clone", $remote, $verifyRoot) -Log (Join-Path $marksLogs ("recovery-" + $marksRunStamp + "-clone.log"))
        $treeOutput = @(& git -C $verifyRoot rev-parse "HEAD:src/MarksApi")
        if ($LASTEXITCODE -ne 0 -or ($treeOutput -join "").Trim() -ne $expectedTree) {
            throw "The GitHub copy does not contain the committed API source. Push the assignment source, then rerun recovery."
        }
    }
    $revisionOutput = @(& git -C $verifyRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw "Cannot read the clean checkout revision." }
    $sourceCommit = ($revisionOutput -join "").Trim()
    if ($sourceCommit -notmatch '^[a-fA-F0-9]{40,64}$') { throw "The checkout revision could not be read." }
    $verifyApi = Join-Path $verifyRoot "src\MarksApi"
    $marksProof["sourceCommit"] = $sourceCommit
    $marksProof["sourceTree"] = $expectedTree
    $marksProof["verificationCheckout"] = $verifyRoot
    $marksProof["recoverySource"] = "Owned verification database and clean checkout with matching committed API tree; build and smoke runs repeated in this recovery."
    Save-MarksCheckpoint

    while (Test-MarksApiPort) {
        Write-Host "Backend wali doosri window mein Ctrl+C dabayein. Is recovery window ko khula rakhein."
        [void](Read-Host "Backend stop karne ke baad yahan sirf Enter dabayein")
    }

    Push-Location $verifyApi
    try {
        $marksProof["stage"] = "build-and-smoke"
        Save-MarksCheckpoint
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("restore") -Log (Join-Path $marksLogs ("recovery-" + $marksRunStamp + "-restore.log"))
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("build", "--no-restore") -Log (Join-Path $marksLogs ("recovery-" + $marksRunStamp + "-build.log"))
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("ef", "database", "update", "--", "--environment", "Development") -Log (Join-Path $marksLogs ("recovery-" + $marksRunStamp + "-migrate.log"))
        Invoke-MarksCommand -Executable "dotnet" -Arguments @("run", "--no-build", "--launch-profile", "https", "--", "--SeedData", "true") -Log (Join-Path $marksLogs ("recovery-" + $marksRunStamp + "-seed.log"))
        $initialCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
        if ($initialCounts -ne "2|3|6|3") { throw "Unexpected verification seed counts: $initialCounts" }
        $marksProof["buildExitCode"] = 0
        $marksProof["seedCounts"] = $initialCounts
        $smokeSequenceBefore = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
        $smokeResults = @()
        foreach ($run in @(1,2)) {
            $log = Join-Path $marksLogs ("recovery-" + $marksRunStamp + "-smoke-" + $run + ".log")
            Invoke-MarksCommand -Executable "dotnet" -Arguments @("run", "--no-build", "--launch-profile", "https", "--", "--smoke-test") -Log $log
            $summary = [regex]::Match((Get-Content -LiteralPath $log -Raw), "SMOKE SUMMARY: (\d+) passed, (\d+) failed")
            if (-not $summary.Success -or [int]$summary.Groups[1].Value -lt 107 -or [int]$summary.Groups[2].Value -ne 0) {
                throw "The real smoke run did not produce a passing summary."
            }
            $smokeResults += @{ run=$run; passed=[int]$summary.Groups[1].Value; failed=0; exitCode=0; log=(Split-Path $log -Leaf) }
            $marksProof["smokeRuns"] = $smokeResults
            Save-MarksCheckpoint
        }
        $afterSmokeCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
        if ($afterSmokeCounts -ne "2|3|6|3") { throw "Smoke cleanup counts differ from the seed counts: $afterSmokeCounts" }
        $smokeSequenceAfter = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
        if ($smokeSequenceAfter -le $smokeSequenceBefore) { throw "The smoke runs did not create fixtures in the selected verification database. Inspect its connection settings." }
        $marksProof["afterSmokeRowCounts"] = $afterSmokeCounts
        $marksProof["smokeSequenceAdvanced"] = $true
        $marksProof["stage"] = "smoke-screenshot"
        Save-MarksCheckpoint
        Clear-Host
        Write-Host "ACTUAL SMOKE OUTPUT - RECOVERED VERIFICATION DATABASE / CLEAN CHECKOUT"
        Get-Content -LiteralPath $log -Tail 12 | ForEach-Object { Write-Host $_ }
        Write-Host "Both real smoke runs exited 0. Verified row counts after cleanup: $afterSmokeCounts"
        [void](Read-Host "Screenshot save karne ke liye yahan sirf Enter dabayein")
        Save-MarksWindow -Handle ([MarksFinishCapture]::GetForegroundWindow()) -Path (Join-Path $marksEvidence "smoke-tests.png")
        $sequenceBefore = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
        $marksProof["postmanSequenceBefore"] = $sequenceBefore
        $marksProof["stage"] = "awaiting-postman"
        Save-MarksCheckpoint

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
        if (-not $healthy) { throw "The verification API did not become healthy. Inspect its new terminal." }
        Write-Host "AB POSTMAN KHOLEN: Local select karein, poori Marks API collection Run/Rerun karein."
        Write-Host "Expected: Passed 118, Failed 0, Errors 0. Run results khule rakhein."
        while ($true) {
            $confirmed = (Read-Host "Postman mein 118 PASS hone par yahan 118 likhein (Q to stop)").Trim()
            if ($confirmed -eq "118") { break }
            if ($confirmed -eq "Q") { throw "Postman verification is pending; the recovery checkpoint has been saved." }
            Write-Host "Sirf Enter se aage nahi jayega. Pehle Postman Run/Rerun karein; 118 pass hone par 118 likhein."
        }
        $finalCounts = Invoke-MarksSql -Database $freshDb -Sql $countsSql
        $sequenceAfter = [long](Invoke-MarksSql -Database $freshDb -Sql "SELECT last_value FROM public.students_id_seq;")
        if ($finalCounts -ne "2|3|6|3" -or $sequenceAfter -le $sequenceBefore) {
            throw "The fresh database does not show a completed Postman fixture lifecycle. Check Local baseUrl is https://localhost:7134 and rerun the complete collection."
        }
        $postmanWindow = Get-Process -Name Postman -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if (-not $postmanWindow) { throw "Open the Postman Desktop run-results window before saving evidence." }
        Save-MarksWindow -Handle $postmanWindow.MainWindowHandle -Path (Join-Path $marksEvidence "postman-runner.png")
        $marksProof["postman"] = @{ requests=59; passed=118; failed=0; errors=0; source="operator confirmation, captured window and verified fresh-database fixture lifecycle" }
        $marksProof["finalRowCounts"] = $finalCounts
        $marksProof["studentSequenceAdvanced"] = $true
        $marksProof["verifiedAtUtc"] = [DateTime]::UtcNow.ToString("o")
        $marksProof["status"] = "passed"
        $marksProof["stage"] = "verified"
    } finally { Pop-Location }

    Restore-MarksEnvironment
    $proofFile = Join-Path $marksRoot "docs\verification-results.json"
    Save-MarksCheckpoint
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
    $marksFailureMessage = $_.Exception.Message
    Write-Host ("ERROR LINE: " + $_.InvocationInfo.ScriptLineNumber)
    $marksProof["status"] = "incomplete"
    $marksProof["failureType"] = $_.Exception.GetType().Name
    $errorRecord = Join-Path $marksLogs ("recovery-error-" + (Get-Date -Format "yyyyMMddHHmmss") + ".json")
    try { $marksProof | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $errorRecord -Encoding UTF8 } catch { }
    Write-Host ("STOPPED: " + $marksFailureMessage) -ForegroundColor Red
    Write-Host "The previous verification record is backed up; the last complete checkpoint was retained."
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

