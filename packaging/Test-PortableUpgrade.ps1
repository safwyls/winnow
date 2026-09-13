[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PreviousArchive,
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -cne 'true') { throw 'Portable upgrade smoke runs only on disposable GitHub Actions runners.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-portable-smoke-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$helperName = if ($Runtime -eq 'win-x64') { 'Winnow.Update.Helper.exe' } else { 'Winnow.Update.Helper' }
$executableName = if ($Runtime -eq 'win-x64') { 'Winnow.exe' } else { 'Winnow' }
$helper = Join-Path (Resolve-Path -LiteralPath $PublishDirectory).Path "update-helper/$helperName"
$Archive = (Resolve-Path -LiteralPath $Archive).Path
$PreviousArchive = (Resolve-Path -LiteralPath $PreviousArchive).Path
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
function Start-SmokeProcess([string]$File, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $processes.Add($process)
    return $process
}
function Stop-SmokeProcess($Process) {
    if (-not $Process.HasExited) { $Process.Kill($true); $Process.WaitForExit() }
}
function Invoke-Helper([string[]]$Arguments, [bool]$ExpectFailure = $false) {
    # Capturing native output waits for inherited pipes held by the relaunched app.
    # Wait for the helper itself, allowing archive IO and its two-minute readiness check.
    $helperTimeout = [TimeSpan]::FromMinutes(5)
    $process = Start-SmokeProcess $helper $Arguments
    if (-not $process.WaitForExit([int]$helperTimeout.TotalMilliseconds)) {
        throw "Portable helper '$($Arguments[0])' did not exit within $($helperTimeout.TotalMinutes) minutes (PID $($process.Id))."
    }
    $code = $process.ExitCode
    if (($code -eq 0) -eq $ExpectFailure) { throw "Unexpected helper exit ${code}: $($Arguments[0])" }
}
function Read-LibraryEvidence([string]$Database) {
    $result = & python -c 'import sqlite3,sys,json; c=sqlite3.connect(sys.argv[1]); assert c.execute("PRAGMA integrity_check").fetchone()[0]=="ok"; print(json.dumps(c.execute("SELECT id,name FROM works ORDER BY id").fetchall()))' $Database
    if ($LASTEXITCODE -ne 0 -or -not $result -or $result -eq '[]') { throw 'Disposable seeded library integrity or contents check failed.' }
    return $result
}
try {
    foreach ($scenario in @('external-data', 'internal-data', 'failed-startup', 'interrupted-replacement')) {
        $inside = $scenario -in @('internal-data', 'interrupted-replacement')
        $scenarioRoot = Join-Path $root $scenario
        $install = Join-Path $scenarioRoot 'portable'
        $null = New-Item -ItemType Directory -Path $install -Force
        if ($Runtime -eq 'win-x64') {
            Expand-Archive -LiteralPath $PreviousArchive -DestinationPath $install
        } else {
            & tar -xzf $PreviousArchive --strip-components=1 -C $install
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract previous portable archive.' }
        }
        $data = if ($inside) { Join-Path $install 'user-data' } else { Join-Path $scenarioRoot 'user-data' }
        $old = Start-SmokeProcess (Join-Path $install $executableName) @('--data-dir', $data, '--no-sync')
        $database = Join-Path $data 'winnow.db'
        for ($attempt = 0; $attempt -lt 120 -and -not (Test-Path -LiteralPath $database); $attempt++) {
            if ($old.HasExited) { throw 'Previous release failed before creating its disposable library.' }
            Start-Sleep -Milliseconds 250
        }
        if (-not (Test-Path -LiteralPath $database)) { throw 'Previous release did not initialize a database.' }
        Start-Sleep -Seconds 3
        if ($old.HasExited) { throw 'Previous release failed after database initialization.' }
        Stop-SmokeProcess $old
        & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO works(id,name) VALUES (?,?)", (-159,"Portable upgrade fixture")); c.commit(); c.close()' $database
        if ($LASTEXITCODE -ne 0) { throw 'Could not seed the disposable previous-release library.' }
        $libraryBefore = Read-LibraryEvidence $database
        $preserved = @('covers/sentinel.bin', 'themes/sentinel.bin', 'webview/sentinel.bin', 'credentials-sentinel.bin')
        foreach ($relative in $preserved) {
            $path = Join-Path $data $relative
            $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
            [IO.File]::WriteAllText($path, 'preserve these user-owned bytes')
        }
        $oldHash = (Get-FileHash -LiteralPath (Join-Path $install 'Winnow.dll')).Hash
        $scenarioArchive = $Archive
        if ($scenario -eq 'failed-startup') {
            # A deliberately broken release fixture, hashed before staging, exercises
            # a real apphost startup failure without bypassing stage verification.
            $broken = Join-Path $scenarioRoot 'broken-release'
            $null = New-Item -ItemType Directory -Path $broken
            if ($Runtime -eq 'win-x64') {
                Expand-Archive -LiteralPath $Archive -DestinationPath $broken
                [IO.File]::WriteAllText((Join-Path $broken 'Winnow.runtimeconfig.json'), '{ invalid JSON')
                $scenarioArchive = Join-Path $scenarioRoot 'broken.zip'
                [IO.Compression.ZipFile]::CreateFromDirectory($broken, $scenarioArchive)
            } else {
                & tar -xzf $Archive -C $broken
                if ($LASTEXITCODE -ne 0) { throw 'Could not extract failed-startup fixture.' }
                $packages = @(Get-ChildItem -LiteralPath $broken -Directory)
                if ($packages.Count -ne 1) { throw 'Expected a single portable archive root.' }
                $package = $packages[0]
                [IO.File]::WriteAllText((Join-Path $package.FullName 'Winnow.runtimeconfig.json'), '{ invalid JSON')
                $scenarioArchive = Join-Path $scenarioRoot 'broken.tar.gz'
                & tar -czf $scenarioArchive -C $broken $package.Name
                if ($LASTEXITCODE -ne 0) { throw 'Could not package failed-startup fixture.' }
            }
        }
        $stageArguments = @('stage', '--archive', $scenarioArchive, '--sha256', ('0' * 64), '--version', $Version,
            '--runtime', $Runtime, '--installation', $install, '--data-dir', $data, '--executable', $executableName, '--no-sync')
        Invoke-Helper $stageArguments $true
        if ((Get-FileHash -LiteralPath (Join-Path $install 'Winnow.dll')).Hash -ne $oldHash) { throw 'Bad digest changed existing binaries.' }
        $stageArguments[4] = (Get-FileHash -LiteralPath $scenarioArchive -Algorithm SHA256).Hash
        Invoke-Helper $stageArguments
        $journal = Join-Path $scenarioRoot '.portable.winnow-update/journal.json'
        if ($scenario -eq 'interrupted-replacement') {
            $workspace = Split-Path $journal -Parent
            & python -c 'import sqlite3,sys; s=sqlite3.connect(sys.argv[1]); d=sqlite3.connect(sys.argv[2]); s.backup(d); d.close(); s.close()' $database (Join-Path $workspace 'before.db')
            if ($LASTEXITCODE -ne 0) { throw 'Could not prepare interrupted replacement backup.' }
            $cut = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
            $cut.DatabaseExisted = $true
            $cut.BackupCompleted = $true
            $cut.Phase = 2
            $cut | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $journal -Encoding utf8NoBOM
            # Reproduce the durable cut immediately after moving the old directory.
            $previous = Join-Path $workspace 'previous'
            foreach ($target in @($install, $previous)) {
                if (-not [IO.Path]::GetFullPath($target).StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Interruption fixture escaped the disposable workspace.'
                }
            }
            Move-Item -LiteralPath $install -Destination $previous
            Invoke-Helper @('recover', '--journal', $journal)
            $recovered = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
            if ($recovered.Phase -ne 8 -or (Get-FileHash -LiteralPath (Join-Path $install 'Winnow.dll')).Hash -ne $oldHash -or
                (Read-LibraryEvidence $database) -cne $libraryBefore) { throw 'Interrupted replacement did not recover prior binaries and internal data.' }
            Write-Host "Passed durable replacement interruption recovery with internal data ($Runtime)."
            continue
        }
        Invoke-Helper @('apply', '--journal', $journal) ($scenario -eq 'failed-startup')
        # PayloadHashes can contain both Winnow and winnow on Linux.
        $state = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
        if ($scenario -eq 'failed-startup') {
            if ($state.Phase -ne 6 -or -not $state.Failure) { throw 'Failed startup did not leave actionable recovery state.' }
            if ((Get-FileHash -LiteralPath (Join-Path $install 'Winnow.dll')).Hash -eq $oldHash) { throw 'Failed startup silently rolled back binaries.' }
            Invoke-Helper @('recover', '--journal', $journal) $true
            Invoke-Helper @('recover', '--journal', $journal, '--restore-backup')
            $restored = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
            if ($restored.Phase -ne 8 -or (Get-FileHash -LiteralPath (Join-Path $install 'Winnow.dll')).Hash -ne $oldHash) {
                throw 'Explicit recovery did not restore the paired previous binaries.'
            }
            if ((Read-LibraryEvidence $database) -cne $libraryBefore) { throw 'Explicit recovery did not preserve the paired library.' }
            Write-Host "Passed real apphost failed startup and explicit backup recovery ($Runtime)."
            continue
        }
        # Apply succeeds only after the actual new app reports migration/host/framework readiness.
        if ($state.Failure -or $state.Phase -ne 5 -or -not $state.MigrationMayHaveStarted) { throw "New app did not acknowledge readiness: $($state.Failure)" }
        $newManifest = Get-Content -LiteralPath (Join-Path $install 'release-info.json') -Raw | ConvertFrom-Json
        if ($newManifest.version -cne $Version) { throw 'Replacement did not install the requested release.' }
        if ((Get-FileHash -LiteralPath (Join-Path $install 'Winnow.dll')).Hash -eq $oldHash) { throw 'The upgrade did not change the application assembly.' }
        foreach ($relative in $preserved) {
            if ([IO.File]::ReadAllText((Join-Path $data $relative)) -cne 'preserve these user-owned bytes') { throw "Upgrade lost $relative." }
        }
        if (-not (Test-Path -LiteralPath $database)) { throw 'Upgrade lost the library database.' }
        if ((Read-LibraryEvidence $database) -cne $libraryBefore) { throw 'Upgrade changed seeded library identities or titles.' }
        Write-Host "Passed actual older-to-newer portable upgrade: $scenario ($Runtime)."
    }
} finally {
    foreach ($process in $processes) { Stop-SmokeProcess $process }
    # Relaunched applications have a different PID. Restrict cleanup by exact executable path.
    Get-Process -Name Winnow -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Path -and $_.Path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-SmokeProcess $_ }
    }
    $diagnostics = Join-Path $PSScriptRoot '../artifacts/portable-smoke-logs'
    $null = New-Item -ItemType Directory -Path $diagnostics -Force
    Get-ChildItem -LiteralPath $root -Filter journal.json -Recurse -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $diagnostics ($_.Directory.Parent.Name + '-journal.json'))
    }
    Get-ChildItem -LiteralPath $root -Filter '*.log' -Recurse -Force | ForEach-Object {
        $name = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('/', '_').Replace('\', '_')
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $diagnostics $name)
    }
    Write-Host "Disposable smoke data retained at $root until runner disposal."
}
