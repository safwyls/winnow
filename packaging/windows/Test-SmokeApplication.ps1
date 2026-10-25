[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Smoke-Application.ps1')

# Impossible PID fixtures exercise failure boundaries without opening or closing any UI.
$directory = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-window-contract-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $directory
$process = [pscustomobject]@{
    Id = [uint32]::MaxValue; Path = 'C:\fixture\Winnow.exe'
    StartTime = [datetime]'2026-10-01T00:00:00Z'; HasExited = $false; ExitCode = 3
}
$process | Add-Member ScriptMethod Refresh {}
try {
    $report = Join-Path $directory 'pending.json'
    $refused = $false
    try { Close-SmokeApplication $process -Report $report -TimeoutSeconds 1 }
    catch { if ($_.Exception.Message -notlike '*within 1 seconds*') { throw }; $refused = $true }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $refused -or $result.ready -or $result.closeSent -or $result.backendHealthy -or
        @($result.windows).Count -ne 0 -or $result.elapsedMilliseconds -lt 1000 -or $result.elapsedMilliseconds -gt 4000 -or
        $result.processId -ne $process.Id -or $result.processStartTicks -ne $process.StartTime.ToUniversalTime().Ticks.ToString()) {
        throw 'Pending startup must time out with exact process evidence and no close/readiness claim.'
    }
    Write-Host 'Pending startup waits for the bound and records the missing window without claiming readiness.'

    $process.HasExited = $true
    $report = Join-Path $directory 'exited.json'
    $refused = $false
    try { Close-SmokeApplication $process -Report $report -TimeoutSeconds 1 }
    catch { if ($_.Exception.Message -notlike '*before its application window was ready (code 3)*') { throw }; $refused = $true }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $refused -or $result.ready -or $result.closeSent -or $result.exitCode -ne 3 -or $result.elapsedMilliseconds -ge 1000) {
        throw 'A failed startup must retain its exit code and stop the readiness wait.'
    }
    Write-Host 'Exited startup fails immediately and retains exit code 3 without claiming readiness.'

    # Extract only failure cleanup. Every process operation below is an in-memory port;
    # the installer entry point and actual process termination are never invoked.
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-WindowsInstaller.ps1'), [ref]$null, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    $cleanupFunction = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Stop-InstallerSmokeBackend' }, $true)
    if (-not $cleanupFunction) { throw 'Isolated failure cleanup function was not found.' }
    Invoke-Expression $cleanupFunction.Extent.Text
    $installDirectory = Join-Path $directory 'installed'
    $dataDirectory = Join-Path $directory 'data with spaces'
    $diagnosticsDirectory = Join-Path $directory 'diagnostics'
    $backendPath = Join-Path $installDirectory 'backend/Winnow.Backend.exe'
    $started = [datetime]'2026-10-01T00:00:00Z'
    $script:killed = @()
    $script:readProcesses = @()
    $backendFixture = [pscustomobject]@{ Id = 41; Path = $backendPath; StartTime = $started; Waits = 0 }
    $backendFixture | Add-Member ScriptMethod WaitForExit { param($timeout); $this.Waits++; return $this.Waits -gt 1 }
    function Stop-SmokeBackend([string]$DataDirectory) {}
    function Get-CimInstance([string]$ClassName, [string]$Filter) {
        @(
            [pscustomobject]@{ ProcessId = 41; ExecutablePath = $backendPath; CommandLine = 'backend --data-dir "' + $dataDirectory + '" --no-sync'; CreationDate = $started },
            [pscustomobject]@{ ProcessId = 42; ExecutablePath = $backendPath; CommandLine = 'backend --data-dir "' + $dataDirectory + '-other"'; CreationDate = $started },
            [pscustomobject]@{ ProcessId = 43; ExecutablePath = 'C:\unrelated\Winnow.Backend.exe'; CommandLine = 'backend --data-dir "' + $dataDirectory + '"'; CreationDate = $started }
        )
    }
    function Get-Process([int]$Id, [string]$ErrorAction) { $script:readProcesses += $Id; return $backendFixture }
    function Stop-Process([int]$Id, [switch]$Force) { $script:killed += $Id }
    Stop-InstallerSmokeBackend
    $cleanup = Get-Content -LiteralPath (Join-Path $diagnosticsDirectory 'backend-cleanup.json') -Raw | ConvertFrom-Json
    if (($script:killed -join ',') -cne '41' -or ($script:readProcesses -join ',') -cne '41' -or
        $cleanup.discoveryPresent -or $cleanup.processes.Count -ne 1 -or -not $cleanup.processes[0].forced -or -not $cleanup.processes[0].exited) {
        throw 'Failure cleanup must find the exact orphan before discovery and never select another data root or installation.'
    }
    Write-Host 'Failure cleanup selects only the exact installation/data root before discovery and records its forced cleanup separately.'

    # Execute the actual two helper launch expressions against a capture-only process
    # port: the restarted GUI must be normal while silent Setup remains hidden.
    $updateAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '../../src/Winnow.App/Services/Install-Update.ps1'), [ref]$null, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    $launches = @($updateAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Start-Process' }, $true))
    $restart = @($launches | Where-Object { $_.Extent.Text.Contains('-FilePath $handoff.Executable') })
    $setup = @($launches | Where-Object { $_.Extent.Text.Contains('-FilePath $handoff.Installer') })
    if ($restart.Count -ne 1 -or $setup.Count -ne 1) { throw 'The updater GUI and Setup launch expressions must each be unique.' }
    $handoff = [pscustomobject]@{ Executable = 'C:\fixture with spaces\Winnow.exe'; InstallDirectory = 'C:\fixture with spaces'; Installer = 'C:\fixture\setup.exe' }
    $restartArgs = @('"--data-dir"', '"C:\data with spaces"', '"--no-sync"')
    $setupArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES')
    function Start-Process([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory, [string]$WindowStyle, [switch]$PassThru, [switch]$Wait) {
        [pscustomobject]@{ FilePath = $FilePath; Arguments = $ArgumentList; WorkingDirectory = $WorkingDirectory; WindowStyle = $WindowStyle; PassThru = $PassThru.IsPresent; Wait = $Wait.IsPresent }
    }
    $captured = Invoke-Expression $restart[0].Extent.Text
    if ($captured.WindowStyle -cne 'Normal' -or -not $captured.PassThru -or $captured.Wait -or
        $captured.FilePath -cne $handoff.Executable -or $captured.WorkingDirectory -cne $handoff.InstallDirectory -or
        ($captured.Arguments -join '|') -cne ($restartArgs -join '|')) {
        throw 'Updater restart must show the actual GUI while retaining its executable, directory and exact arguments.'
    }
    $captured = Invoke-Expression $setup[0].Extent.Text
    if ($captured.WindowStyle -cne 'Hidden' -or -not $captured.Wait -or -not $captured.PassThru -or
        $captured.FilePath -cne $handoff.Installer -or ($captured.Arguments -join '|') -cne ($setupArgs -join '|')) {
        throw 'Silent Setup must remain hidden and awaited.'
    }
    Write-Host 'Actual updater launch expressions show the restarted GUI and keep silent Setup hidden, preserving exact arguments.'
} finally {
    $resolved = [IO.Path]::GetFullPath($directory)
    $prefix = Join-Path ([IO.Path]::GetTempPath()) 'Winnow-window-contract-'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $resolved -notmatch 'Winnow-window-contract-[0-9a-f]{32}$') {
        throw 'Window contract cleanup escaped its temporary directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
