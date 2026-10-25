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
. (Join-Path $PSScriptRoot 'Stop-SmokeBackend.ps1')
. (Join-Path $PSScriptRoot 'Portable-SmokeEvidence.ps1')
. (Join-Path $PSScriptRoot 'windows/Test-ElectronPackageLayout.ps1')
if ($env:GITHUB_ACTIONS -cne 'true') { throw 'Portable upgrade smoke runs only on disposable GitHub Actions runners.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-portable-smoke-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$helperName = if ($Runtime -eq 'win-x64') { 'Winnow.Update.Helper.exe' } else { 'Winnow.Update.Helper' }
$executableName = if ($Runtime -eq 'win-x64') { 'Winnow.exe' } else { 'Winnow' }
$currentHelper = Join-Path (Resolve-Path -LiteralPath $PublishDirectory).Path "update-helper/$helperName"
$helper = $currentHelper
$Archive = (Resolve-Path -LiteralPath $Archive).Path
$PreviousArchive = (Resolve-Path -LiteralPath $PreviousArchive).Path
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$dataDirectories = [Collections.Generic.List[string]]::new()
$replacementWitnesses = [Collections.Generic.Dictionary[string,object]]::new()
$sandboxPaths = [Collections.Generic.List[string]]::new()
$sandboxSetup = Join-Path $PSScriptRoot 'linux/setup-sandbox.sh'
$targetPayload = Get-WinnowFrontendPayload $PublishDirectory
function Start-SmokeProcess([string]$File, [string[]]$Arguments, [switch]$Frontend) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = if ($Frontend) { [Diagnostics.ProcessWindowStyle]::Normal } else { [Diagnostics.ProcessWindowStyle]::Hidden }
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
    $call = [ordered]@{
        command = $Arguments[0]; executable = $process.StartInfo.FileName; processId = $process.Id
        implementation = if ($scenario -eq 'previous-helper') { 'previous-release' } else { 'current-release' }
        hostSha256 = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash.ToLowerInvariant()
        expectedFailure = $ExpectFailure; exited = $false; exitCode = $null
    }
    $helperCalls.Add($call)
    ConvertTo-Json -InputObject @($helperCalls.ToArray()) -Depth 5 | Set-Content -LiteralPath $helperCallsPath -Encoding utf8
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $watchedJournal = $null
    if ($IsLinux -and $Arguments[0] -ceq 'apply') {
        $journalIndex = [Array]::IndexOf($Arguments, '--journal')
        if ($journalIndex -lt 0 -or $journalIndex + 1 -ge $Arguments.Count) { throw 'Apply has no journal.' }
        $watchedJournal = $Arguments[$journalIndex + 1]
        $ownedHelper = Get-PortableLinuxProcessIdentity $process.Id
        if ($null -eq $ownedHelper -or $ownedHelper.executable -cne [IO.Path]::GetFullPath($helper)) {
            throw 'Cannot witness the exact owned update helper.'
        }
        $applyState = Get-Content -LiteralPath $watchedJournal -Raw | ConvertFrom-Json -AsHashtable
        $expectedChild = Join-Path $applyState.InstallationDirectory $applyState.ExecutableName
    }
    do {
        if ($watchedJournal -and -not $replacementWitnesses.ContainsKey($watchedJournal) -and -not $process.HasExited) {
            $record = Join-Path (Split-Path $watchedJournal -Parent) 'child-process'
            $witness = Get-PortableReplacementWitness $record $ownedHelper $expectedChild
            if ($null -ne $witness) {
                $replacementWitnesses.Add($watchedJournal, $witness)
                $witness | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "$scenario-child-witness.json") -Encoding utf8
            }
        }
        if ($process.WaitForExit(50)) { break }
        if ($timer.Elapsed -gt $helperTimeout) {
            throw "Portable helper '$($Arguments[0])' did not exit within $($helperTimeout.TotalMinutes) minutes (PID $($process.Id))."
        }
    } while ($true)
    $code = $process.ExitCode
    $call.exited = $true
    $call.exitCode = $code
    ConvertTo-Json -InputObject @($helperCalls.ToArray()) -Depth 5 | Set-Content -LiteralPath $helperCallsPath -Encoding utf8
    if (($code -eq 0) -eq $ExpectFailure) {
        $detail = ''
        $journalIndex = [Array]::IndexOf($Arguments, '--journal')
        if ($journalIndex -ge 0 -and $journalIndex + 1 -lt $Arguments.Count) {
            try {
                $failedState = Get-Content -LiteralPath $Arguments[$journalIndex + 1] -Raw | ConvertFrom-Json -AsHashtable
                $detail = " Phase=$($failedState.Phase); Failure=$($failedState.Failure)"
                $startupLogs = Join-Path $failedState.DataDirectory 'logs'
                if (Test-Path -LiteralPath $startupLogs) {
                    Get-ChildItem -LiteralPath $startupLogs -Filter 'startup-failure*.log' | ForEach-Object {
                        Get-Content -LiteralPath $_.FullName -Tail 10 | ForEach-Object { Write-Host $_ }
                    }
                }
            } catch {
                $detail = " Could not read startup diagnostics: $($_.Exception.Message)"
            }
        }
        throw "Unexpected helper exit ${code}: $($Arguments[0]) ($scenario).$detail"
    }
}
function Copy-PreviousPortableHelper([string]$Installation, [string]$Destination, [string]$Runtime) {
    $installationPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Installation))
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ($destinationPath.Equals($installationPath, $comparison) -or
        $destinationPath.StartsWith($installationPath + [IO.Path]::DirectorySeparatorChar, $comparison) -or
        (Test-Path -LiteralPath $destinationPath)) {
        throw 'The previous helper copy must be new and outside the installation being replaced.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $installationPath 'release-info.json') -Raw | ConvertFrom-Json
    if ($manifest.runtime -cne $Runtime) { throw 'Previous helper runtime differs from the selected portable release.' }
    $hostName = if ($Runtime -eq 'win-x64') { 'Winnow.Update.Helper.exe' } elseif ($Runtime -eq 'linux-x64') { 'Winnow.Update.Helper' } else { throw 'Unsupported previous helper runtime.' }
    $source = Join-Path $installationPath 'update-helper'
    foreach ($required in @($hostName, 'Winnow.Update.Helper.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) {
            throw "The selected previous release has no bundled $required; current-helper fallback is forbidden."
        }
    }
    $files = @(Get-ChildItem -LiteralPath $source -Recurse -File -Force)
    if (@(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Previous helper bundle contains a linked entry.'
    }
    Copy-Item -LiteralPath $source -Destination $destinationPath -Recurse
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath (Join-Path $destinationPath $relative) -Algorithm SHA256).Hash) {
            throw "Copied previous helper differs at $relative."
        }
    }
    [pscustomobject]@{
        version = $manifest.version; runtime = $manifest.runtime
        sourceDirectory = $source; copiedDirectory = $destinationPath; copiedFiles = $files.Count
        executable = Join-Path $destinationPath $hostName
        hostSha256 = (Get-FileHash -LiteralPath (Join-Path $destinationPath $hostName) -Algorithm SHA256).Hash.ToLowerInvariant()
        assemblySha256 = (Get-FileHash -LiteralPath (Join-Path $destinationPath 'Winnow.Update.Helper.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        roles = @('stage', 'apply'); frontendReadiness = 'replacement release frontend guard'
    }
}
function Read-LibraryEvidence([string]$Database) {
    $result = & python -c 'import sqlite3,sys,json; c=sqlite3.connect(sys.argv[1]); assert c.execute("PRAGMA integrity_check").fetchone()[0]=="ok"; print(json.dumps(c.execute("SELECT id,name FROM works ORDER BY id").fetchall()))' $Database
    if ($LASTEXITCODE -ne 0 -or -not $result -or $result -eq '[]') { throw 'Disposable seeded library integrity or contents check failed.' }
    return $result
}
function Assert-PreservedPortableFiles([string]$DataDirectory, [string[]]$RelativePaths) {
    foreach ($relative in $RelativePaths) {
        if ([IO.File]::ReadAllText((Join-Path $DataDirectory $relative)) -cne 'preserve these user-owned bytes') {
            throw "Upgrade or recovery lost $relative."
        }
    }
}
function Stop-ReplacedApplication([string]$Journal, [string]$Installation, [string]$DataDirectory) {
    $processRecord = Join-Path (Split-Path $Journal -Parent) 'child-process'
    if (Test-Path -LiteralPath $processRecord) {
        $recordBytes = [IO.File]::ReadAllBytes($processRecord)
        $identity = Read-PortableChildRecord $recordBytes
        $child = Get-Process -Id $identity.processId -ErrorAction SilentlyContinue
        if ($null -ne $child) {
            $expected = [IO.Path]::GetFullPath((Join-Path $Installation $executableName))
            $diagnostic = [ordered]@{
                processId = $child.Id; expectedExecutable = $expected; observedExecutable = $null
                helperUtcTicks = $identity.helperUtcTicks.ToString(); observedUtcTicks = $null
                linux = $null; hasWitness = $replacementWitnesses.ContainsKey($Journal); accepted = $false; errorType = $null
            }
            try {
                $diagnostic.observedExecutable = $child.Path
                $diagnostic.observedUtcTicks = $child.StartTime.ToUniversalTime().Ticks.ToString()
                if ($IsLinux) {
                    $observed = Get-PortableLinuxProcessIdentity $child.Id
                    $diagnostic.linux = $observed
                    $witness = if ($replacementWitnesses.ContainsKey($Journal)) { $replacementWitnesses[$Journal] } else { $null }
                    Assert-PortableReplacementIdentity $witness $recordBytes $observed $expected
                    # Recheck immutable kernel birth/executable immediately before the owned kill.
                    Assert-PortableReplacementIdentity $witness $recordBytes (Get-PortableLinuxProcessIdentity $child.Id) $expected
                } elseif ($child.Path -ine $expected -or $child.StartTime.ToUniversalTime().Ticks -ne $identity.helperUtcTicks) {
                    throw 'The updated application process identity changed; refusing cleanup.'
                }
                $diagnostic.accepted = $true
                Stop-SmokeProcess $child
            } catch {
                $diagnostic.errorType = $_.Exception.GetType().FullName
                throw
            } finally {
                $diagnostic | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "$scenario-child-cleanup.json") -Encoding utf8
            }
        }
    }
    Stop-SmokeBackend $DataDirectory
}
try {
    foreach ($scenario in @('external-data', 'internal-data', 'failed-startup', 'interrupted-replacement', 'previous-helper')) {
        Write-Host "Starting portable upgrade scenario: $scenario ($Runtime)."
        $helper = $currentHelper
        $helperCalls = [Collections.Generic.List[object]]::new()
        $evidenceDirectory = Join-Path $PSScriptRoot '../artifacts/portable-smoke-logs'
        $null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
        $helperCallsPath = Join-Path $evidenceDirectory "$scenario-helper-calls.json"
        $inside = $scenario -in @('internal-data', 'interrupted-replacement')
        $scenarioRoot = Join-Path $root $scenario
        $install = Join-Path $scenarioRoot 'portable'
        $null = New-Item -ItemType Directory -Path $install -Force
        if ($Runtime -eq 'win-x64') {
            Expand-Archive -LiteralPath $PreviousArchive -DestinationPath $install
        } else {
            & tar -xzf $PreviousArchive --strip-components=1 -C $install
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract previous portable archive.' }
            if ($targetPayload.Electron) {
                # The executable path stays stable across replacement. Authorize that
                # exact path before either release starts, without weakening userns policy.
                $sandboxExecutable = Join-Path $install $executableName
                & sudo bash $sandboxSetup --executable $sandboxExecutable
                if ($LASTEXITCODE -ne 0) { throw 'Could not configure the portable Chromium sandbox.' }
                $sandboxPaths.Add($sandboxExecutable)
            }
        }
        $data = if ($inside) { Join-Path $install 'user-data' } else { Join-Path $scenarioRoot 'user-data' }
        $dataDirectories.Add($data)
        $old = Start-SmokeProcess (Join-Path $install $executableName) @('--data-dir', $data, '--no-sync') -Frontend
        $database = Join-Path $data 'winnow.db'
        for ($attempt = 0; $attempt -lt 120 -and -not (Test-Path -LiteralPath $database); $attempt++) {
            if ($old.HasExited) { throw 'Previous release failed before creating its disposable library.' }
            Start-Sleep -Milliseconds 250
        }
        if (-not (Test-Path -LiteralPath $database)) { throw 'Previous release did not initialize a database.' }
        Start-Sleep -Seconds 3
        if ($old.HasExited) { throw 'Previous release failed after database initialization.' }
        Stop-SmokeProcess $old
        Stop-SmokeBackend $data
        if ($scenario -eq 'previous-helper') {
            # The released app copies its own helper before replacing its installation.
            # Keep this entire bundle outside that tree; never substitute the new helper.
            $previousHelper = Copy-PreviousPortableHelper $install (Join-Path $scenarioRoot 'previous-helper') $Runtime
            $previousHelper | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'previous-helper.json') -Encoding utf8
            $helper = $previousHelper.executable
        }
        & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO works(id,name) VALUES (?,?)", (-159,"Portable upgrade fixture")); c.commit(); c.close()' $database
        if ($LASTEXITCODE -ne 0) { throw 'Could not seed the disposable previous-release library.' }
        $libraryBefore = Read-LibraryEvidence $database
        $preserved = @('covers/sentinel.bin', 'themes/sentinel.bin', 'webview/sentinel.bin', 'credentials-sentinel.bin')
        foreach ($relative in $preserved) {
            $path = Join-Path $data $relative
            $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
            [IO.File]::WriteAllText($path, 'preserve these user-owned bytes')
        }
        $oldPayload = Get-WinnowFrontendPayload $install
        $scenarioArchive = $Archive
        if ($scenario -eq 'failed-startup') {
            # A deliberately broken release fixture, hashed before staging, exercises
            # a real apphost startup failure without bypassing stage verification.
            $broken = Join-Path $scenarioRoot 'broken-release'
            $null = New-Item -ItemType Directory -Path $broken
            if ($Runtime -eq 'win-x64') {
                Expand-Archive -LiteralPath $Archive -DestinationPath $broken
                $brokenPayload = Get-WinnowFrontendPayload $broken
                $brokenTarget = if ($brokenPayload.Electron) { $brokenPayload.RelativePath } else { 'Winnow.runtimeconfig.json' }
                [IO.File]::WriteAllText((Join-Path $broken $brokenTarget), '{ invalid startup payload')
                $scenarioArchive = Join-Path $scenarioRoot 'broken.zip'
                [IO.Compression.ZipFile]::CreateFromDirectory($broken, $scenarioArchive)
            } else {
                & tar -xzf $Archive -C $broken
                if ($LASTEXITCODE -ne 0) { throw 'Could not extract failed-startup fixture.' }
                $packages = @(Get-ChildItem -LiteralPath $broken -Directory)
                if ($packages.Count -ne 1) { throw 'Expected a single portable archive root.' }
                $package = $packages[0]
                $brokenPayload = Get-WinnowFrontendPayload $package.FullName
                $brokenTarget = if ($brokenPayload.Electron) { $brokenPayload.RelativePath } else { 'Winnow.runtimeconfig.json' }
                [IO.File]::WriteAllText((Join-Path $package.FullName $brokenTarget), '{ invalid startup payload')
                $scenarioArchive = Join-Path $scenarioRoot 'broken.tar.gz'
                & tar -czf $scenarioArchive -C $broken $package.Name
                if ($LASTEXITCODE -ne 0) { throw 'Could not package failed-startup fixture.' }
            }
        }
        $stageArguments = @('stage', '--archive', $scenarioArchive, '--sha256', ('0' * 64), '--version', $Version,
            '--runtime', $Runtime, '--installation', $install, '--data-dir', $data, '--executable', $executableName, '--no-sync')
        Invoke-Helper $stageArguments $true
        if (-not (Test-WinnowSameFrontendPayload (Get-WinnowFrontendPayload $install) $oldPayload)) { throw 'Bad digest changed existing frontend bytes.' }
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
            if ($recovered.Phase -ne 8 -or -not (Test-WinnowSameFrontendPayload (Get-WinnowFrontendPayload $install) $oldPayload) -or
                (Read-LibraryEvidence $database) -cne $libraryBefore) { throw 'Interrupted replacement did not recover prior binaries and internal data.' }
            Assert-PreservedPortableFiles $data $preserved
            Write-Host "Passed durable replacement interruption recovery with internal data ($Runtime)."
            continue
        }
        Invoke-Helper @('apply', '--journal', $journal) ($scenario -eq 'failed-startup')
        # PayloadHashes can contain both Winnow and winnow on Linux.
        $state = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
        if ($scenario -eq 'failed-startup') {
            if ($state.Phase -ne 6 -or -not $state.Failure) { throw 'Failed startup did not leave actionable recovery state.' }
            if (Test-WinnowSameFrontendPayload (Get-WinnowFrontendPayload $install) $oldPayload) { throw 'Failed startup silently rolled back frontend bytes.' }
            # Electron may retain a native startup-error window. Recovery still requires
            # the exact failed child and independent backend to be closed first.
            Stop-ReplacedApplication $journal $install $data
            Invoke-Helper @('recover', '--journal', $journal) $true
            Invoke-Helper @('recover', '--journal', $journal, '--restore-backup')
            $restored = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
            if ($restored.Phase -ne 8 -or -not (Test-WinnowSameFrontendPayload (Get-WinnowFrontendPayload $install) $oldPayload)) {
                throw 'Explicit recovery did not restore the paired previous binaries.'
            }
            if ((Read-LibraryEvidence $database) -cne $libraryBefore) { throw 'Explicit recovery did not preserve the paired library.' }
            Assert-PreservedPortableFiles $data $preserved
            Write-Host "Passed real apphost failed startup and explicit backup recovery ($Runtime)."
            continue
        }
        # Apply succeeds only after the actual new app reports migration/host/framework readiness.
        if ($state.Failure -or $state.Phase -ne 5 -or -not $state.MigrationMayHaveStarted) { throw "New app did not acknowledge readiness: $($state.Failure)" }
        $newManifest = Get-Content -LiteralPath (Join-Path $install 'release-info.json') -Raw | ConvertFrom-Json
        if ($newManifest.version -cne $Version) { throw 'Replacement did not install the requested release.' }
        $newPayload = Get-WinnowFrontendPayload $install
        if (Test-WinnowSameFrontendPayload $newPayload $oldPayload) { throw 'The upgrade did not change the frontend payload.' }
        if (-not (Test-WinnowSameFrontendPayload $newPayload (Get-WinnowFrontendPayload $PublishDirectory))) {
            throw 'The installed frontend does not match the validated new publish directory.'
        }
        if ($newPayload.Electron) {
            if ($Runtime -eq 'win-x64') {
                Assert-WinnowWindowsDirectory $install $data
                Assert-WinnowPackagedHashes $install
            } else {
                Push-Location $install
                try {
                    & sha256sum --check --quiet PACKAGE-SHA256SUMS
                    if ($LASTEXITCODE -ne 0) { throw 'Portable Linux files differ from the verified publish directory.' }
                } finally { Pop-Location }
            }
            Stop-ReplacedApplication $journal $install $data
            $report = Join-Path $PSScriptRoot "../artifacts/portable-smoke-logs/$scenario-electron.json"
            Invoke-WinnowPackagedProbe (Join-Path $install $executableName) $data $report 'desktop'
            if ($Runtime -eq 'linux-x64') {
                $fullscreenReport = Join-Path $PSScriptRoot "../artifacts/portable-smoke-logs/$scenario-electron-fullscreen.json"
                Invoke-WinnowPackagedProbe (Join-Path $install $executableName) $data $fullscreenReport 'fullscreen'
            }
        }
        Assert-PreservedPortableFiles $data $preserved
        if (-not (Test-Path -LiteralPath $database)) { throw 'Upgrade lost the library database.' }
        if ((Read-LibraryEvidence $database) -cne $libraryBefore) { throw 'Upgrade changed seeded library identities or titles.' }
        Write-Host "Passed actual older-to-newer portable upgrade: $scenario ($Runtime)."
    }
} finally {
    foreach ($process in $processes) { Stop-SmokeProcess $process }
    # Relaunched applications have a different PID. Restrict cleanup by exact executable path.
    $cleanupComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    Get-Process -Name Winnow,Winnow.Backend -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Path -and $_.Path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, $cleanupComparison)) { Stop-SmokeProcess $_ }
    }
    $sandboxCleanupFailed = $false
    foreach ($sandboxExecutable in $sandboxPaths) {
        & sudo bash $sandboxSetup --executable $sandboxExecutable --remove
        if ($LASTEXITCODE -ne 0) { $sandboxCleanupFailed = $true }
    }
    $diagnostics = Join-Path $PSScriptRoot '../artifacts/portable-smoke-logs'
    $null = New-Item -ItemType Directory -Path $diagnostics -Force
    Copy-PortableSmokeDiagnostics $root $dataDirectories.ToArray() $diagnostics
    Write-Host "Disposable smoke data retained at $root until runner disposal."
    if ($sandboxCleanupFailed) { throw 'Could not remove a disposable portable AppArmor profile.' }
}
