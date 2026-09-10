[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$InstallerPath,

    [string]$PreviousInstallerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-SilentProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "'$FilePath' exited with code $($process.ExitCode)."
    }
}

function Remove-VerifiedSmokeRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedTempRoot = (Resolve-Path -LiteralPath ([System.IO.Path]::GetTempPath())).Path.TrimEnd('\')
    $expectedPrefix = Join-Path $resolvedTempRoot 'Winnow-installer-smoke-'
    if (-not $resolvedPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPath -notmatch '^.+\\Winnow-installer-smoke-[0-9a-f]{32}$') {
        throw "Refusing to recursively remove a path outside this smoke test's temporary directory: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

if ($env:GITHUB_ACTIONS -cne 'true') {
    throw 'The installer smoke test runs only on a fresh GitHub Actions runner to avoid changing a local installer registration.'
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer does not exist: $InstallerPath"
}
$installerPath = (Resolve-Path -LiteralPath $InstallerPath).Path

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Winnow-installer-smoke-" + [guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $smokeRoot 'installed'
$dataDirectory = Join-Path $smokeRoot 'data'
$applicationPath = Join-Path $installDirectory 'Winnow.exe'
$uninstallerPath = Join-Path $installDirectory 'unins000.exe'
$databasePath = Join-Path $dataDirectory 'winnow.db'
$applicationProcess = $null
$lockedFile = $null
$helper = $null

# Hidden CI windows are not returned by Process.MainWindowHandle. Send the same
# WM_CLOSE to the test process's titled top-level window without making it visible.
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class WinnowSmokeWindow {
    private delegate bool Enumerate(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerate callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static bool Close(uint process) {
        bool sent = false;
        EnumWindows((window, state) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != process) return true;
            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().StartsWith("Winnow", StringComparison.Ordinal)) return true;
            sent = PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return !sent;
        }, IntPtr.Zero);
        return sent;
    }
}
'@
function Close-SmokeApplication($Process) {
    if (-not $Process.CloseMainWindow() -and -not [WinnowSmokeWindow]::Close($Process.Id)) {
        throw "No Winnow window accepted a close request for process $($Process.Id)."
    }
}

try {
    $null = New-Item -ItemType Directory -Path $smokeRoot -Force
    $installerArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-',
        ('/DIR="{0}"' -f $installDirectory)
    )

    $initialInstaller = if ($PreviousInstallerPath) { (Resolve-Path -LiteralPath $PreviousInstallerPath).Path } else { $installerPath }
    Invoke-SilentProcess -FilePath $initialInstaller -Arguments $installerArguments
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "The silent install did not create $applicationPath."
    }

    $applicationArguments = '--no-sync --data-dir "{0}"' -f $dataDirectory
    $applicationProcess = Start-Process -FilePath $applicationPath -ArgumentList ($applicationArguments + ' --seed-sample') -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (Test-Path -LiteralPath $databasePath -PathType Leaf) {
            break
        }
        if ($applicationProcess.HasExited) {
            throw "Winnow exited during the isolated startup check with code $($applicationProcess.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
        throw 'Winnow did not create winnow.db in the isolated --data-dir directory.'
    }
    Start-Sleep -Seconds 2
    if ($applicationProcess.HasExited) {
        throw "Winnow exited after database creation with code $($applicationProcess.ExitCode)."
    }
    Stop-Process -Id $applicationProcess.Id -Force
    if (-not $applicationProcess.WaitForExit(10000)) {
        throw 'Winnow did not stop after the isolated startup check.'
    }
    $applicationProcess = $null

    $sentinelPath = Join-Path $dataDirectory 'preserve-after-uninstall.txt'
    [System.IO.File]::WriteAllText($sentinelPath, 'keep this user data')
    $preservedFiles = @('covers/preserved.bin', 'themes/preserved.bin', 'webview/preserved.bin', 'credentials-smoke.bin')
    foreach ($relativePath in $preservedFiles) {
        $preservedPath = Join-Path $dataDirectory $relativePath
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $preservedPath) -Force
        [IO.File]::WriteAllText($preservedPath, 'preserve these user-owned bytes')
    }

    $helperScript = Join-Path $PSScriptRoot '../../src/Winnow.App/Services/Install-Update.ps1'
    $previousBinaryDigest = (Get-FileHash -LiteralPath $applicationPath -Algorithm SHA256).Hash
    foreach ($scenario in @('bad-digest', 'cancelled', 'shutdown-timeout', 'locked-file', 'upgrade')) {
        Write-Host "Updater smoke scenario: $scenario"
        $scenarioDirectory = Join-Path $smokeRoot $scenario
        $null = New-Item -ItemType Directory -Path $scenarioDirectory
        $applicationProcess = Start-Process -FilePath $applicationPath -ArgumentList $applicationArguments -WindowStyle Hidden -PassThru
        Start-Sleep -Seconds 3
        if ($applicationProcess.HasExited) { throw 'Winnow could not start for the updater smoke test.' }
        $manifest = @{
            ProcessId = $applicationProcess.Id
            ProcessStartTicks = $applicationProcess.StartTime.ToUniversalTime().Ticks.ToString()
            Executable = $applicationPath
            InstallDirectory = $installDirectory
            Installer = $installerPath
            Sha256 = if ($scenario -eq 'bad-digest') { '0' * 64 } else { (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash }
            Arguments = @('--data-dir', $dataDirectory, '--no-sync')
            WaitSeconds = if ($scenario -eq 'shutdown-timeout') { 1 } else { 120 }
        }
        $manifestPath = Join-Path $scenarioDirectory 'handoff.json'
        $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
        if ($scenario -eq 'cancelled') { Set-Content -LiteralPath (Join-Path $scenarioDirectory 'cancel') -Value 'cancelled' }
        $lockedFile = $null
        if ($scenario -eq 'locked-file') {
            $lockedPath = Join-Path $installDirectory 'smoke-locked.dll'
            [IO.File]::WriteAllText($lockedPath, 'locked binary fixture')
            $lockedFile = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        }
        $helper = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @(
            '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $helperScript), '-ManifestPath', ('"{0}"' -f $manifestPath)
        ) -WindowStyle Hidden -PassThru
        if ($scenario -in @('upgrade', 'locked-file')) {
            for ($attempt = 0; $attempt -lt 200 -and -not (Test-Path -LiteralPath (Join-Path $scenarioDirectory 'ready')); $attempt++) {
                if ($helper.HasExited) { throw (Get-Content -LiteralPath (Join-Path $scenarioDirectory 'failure.txt') -Raw) }
                Start-Sleep -Milliseconds 100
            }
            if (-not (Test-Path -LiteralPath (Join-Path $scenarioDirectory 'ready'))) { throw 'Update helper was not ready.' }
            Set-Content -LiteralPath (Join-Path $scenarioDirectory 'proceed') -Value 'ready'
            # The production app requests its normal shutdown after the same handshake.
            Close-SmokeApplication $applicationProcess
            if (-not $applicationProcess.WaitForExit(30000)) { throw 'Winnow did not close normally for the upgrade.' }
        }
        if (-not $helper.WaitForExit(180000)) { throw 'Update helper did not finish.' }
        if ($scenario -ne 'upgrade') {
            if ($helper.ExitCode -eq 0 -or ($scenario -ne 'locked-file' -and $applicationProcess.HasExited) -or
                -not (Test-Path -LiteralPath (Join-Path $scenarioDirectory 'failure.txt'))) {
                throw "The $scenario case did not fail safely while Winnow kept running."
            }
            if ($null -ne $lockedFile) {
                $lockedFile.Dispose()
                Remove-Item -LiteralPath $lockedPath
            }
            if (-not $applicationProcess.HasExited) {
                Close-SmokeApplication $applicationProcess
                if (-not $applicationProcess.WaitForExit(30000)) { throw 'Winnow did not close after failure smoke test.' }
            }
        } else {
            if ($helper.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $scenarioDirectory 'complete'))) {
                throw (Get-Content -LiteralPath (Join-Path $scenarioDirectory 'failure.txt') -Raw)
            }
            $restartedId = Get-Content -LiteralPath (Join-Path $scenarioDirectory 'restarted.json') -Raw | ConvertFrom-Json
            $applicationProcess = Get-Process -Id $restartedId
            if ($applicationProcess.Path -ine $applicationPath) { throw 'The helper relaunched a different app.' }
            $commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $restartedId").CommandLine
            if (-not $commandLine.Contains($dataDirectory) -or -not $commandLine.Contains('--no-sync')) { throw 'Restart arguments lost the selected data directory or no-sync.' }
            Close-SmokeApplication $applicationProcess
            if (-not $applicationProcess.WaitForExit(30000)) { throw 'Updated Winnow did not close.' }
        }
        $applicationProcess = $null
        if ((Get-Content -LiteralPath $sentinelPath -Raw) -cne 'keep this user data') { throw 'Upgrade changed user data.' }
        foreach ($relativePath in $preservedFiles) {
            if ((Get-Content -LiteralPath (Join-Path $dataDirectory $relativePath) -Raw) -cne 'preserve these user-owned bytes') {
                throw "Upgrade changed $relativePath."
            }
        }
        if ($scenario -ne 'upgrade' -and (Get-FileHash -LiteralPath $applicationPath -Algorithm SHA256).Hash -ne $previousBinaryDigest) { throw 'A rejected update changed the installed executable.' }
    }
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw 'The silent reinstall did not preserve the selected install location.'
    }

    if (-not (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) {
        throw "The install did not create its uninstaller: $uninstallerPath"
    }
    Invoke-SilentProcess -FilePath $uninstallerPath -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')

    if (Test-Path -LiteralPath $applicationPath -PathType Leaf) {
        throw 'The silent uninstall left Winnow.exe in the install directory.'
    }
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'The uninstall removed user data outside the install directory.'
    }

    Write-Host "Windows installer smoke test passed: $installerPath"
}
catch {
    Write-Host "Windows updater smoke failed: $($_.Exception.Message)"
    $diagnosticsDirectory = Join-Path $PSScriptRoot '../../artifacts/windows-smoke-logs'
    $null = New-Item -ItemType Directory -Path $diagnosticsDirectory -Force
    Get-ChildItem -LiteralPath $smokeRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'failure.txt' -or $_.Extension -eq '.log' } |
        ForEach-Object {
            if ($_.Name -eq 'failure.txt') { Write-Host (Get-Content -LiteralPath $_.FullName -Raw) }
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $diagnosticsDirectory ($_.Directory.Name + '-' + $_.Name)) -ErrorAction Continue
        }
    throw
}
finally {
    if ($null -ne $lockedFile) { $lockedFile.Dispose() }
    if ($null -ne $helper -and -not $helper.HasExited) {
        Stop-Process -Id $helper.Id -Force -ErrorAction SilentlyContinue
        $null = $helper.WaitForExit(10000)
    }
    if ($null -ne $applicationProcess -and -not $applicationProcess.HasExited) {
        Stop-Process -Id $applicationProcess.Id -Force -ErrorAction SilentlyContinue
        $null = $applicationProcess.WaitForExit(10000)
    }
    try { Remove-VerifiedSmokeRoot -Path $smokeRoot }
    catch { Write-Warning "Disposable smoke directory could not be removed: $($_.Exception.Message)" }
}
