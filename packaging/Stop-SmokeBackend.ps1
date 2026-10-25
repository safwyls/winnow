# Smoke callers supply an isolated data directory; never discover the user's backend.
. (Join-Path $PSScriptRoot 'Portable-SmokeEvidence.ps1')

function Get-SmokeLinuxBackendState([int]$ProcessId) {
    try {
        # Zombies retain stat until reaped, but no longer have an exe link or HTTP listener.
        # https://docs.kernel.org/filesystems/proc.html (stat state and start_time)
        $state = ConvertFrom-PortableProcStat ([IO.File]::ReadAllText("/proc/$ProcessId/stat")) ''
        if ($state.processId -ne $ProcessId) { throw 'Linux backend process identity changed while it was read.' }
        return $state
    }
    catch [IO.FileNotFoundException] { return $null }
    catch [IO.DirectoryNotFoundException] { return $null }
}

function Test-SmokeLinuxBackendStopped($Expected, $Observed) {
    if ($null -eq $Observed) { return $true }
    if ($Observed.processId -ne $Expected.processId -or $Observed.startTicks -ne $Expected.startTicks) {
        Write-SmokeBackendState $Observed.processId $Observed 'identity mismatch'
        throw 'The smoke backend PID was reused; refusing to treat another process as the backend.'
    }
    return $Observed.state -cin @('Z', 'X')
}

function Write-SmokeBackendState([int]$ProcessId, $State, [string]$Phase) {
    $birth = if ($null -eq $State) { 'absent' } else { $State.startTicks.ToString() }
    $status = if ($null -eq $State) { 'absent' } else { $State.state }
    Write-Host "Smoke backend $ProcessId ${Phase}: kernel birth=$birth state=$status"
}

function Wait-SmokeLinuxBackendStopped($Expected, [int]$TimeoutMilliseconds) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $observed = Get-SmokeLinuxBackendState $Expected.processId
        if (Test-SmokeLinuxBackendStopped $Expected $observed) {
            Write-SmokeBackendState $Expected.processId $observed 'shutdown complete'
            return $true
        }
        if ($wait.ElapsedMilliseconds -ge $TimeoutMilliseconds) { break }
        Start-Sleep -Milliseconds ([Math]::Max(1, [Math]::Min(100, $TimeoutMilliseconds - $wait.ElapsedMilliseconds)))
    } while ($true)
    Write-SmokeBackendState $Expected.processId $observed 'shutdown timed out'
    return $false
}

function Stop-SmokeLinuxBackend($Discovery, [uri]$Address, [int]$ShutdownTimeoutMilliseconds = 30000) {
    $backend = Get-SmokeLinuxBackendState $Discovery.processId
    Write-SmokeBackendState $Discovery.processId $backend 'before shutdown'
    if ($null -eq $backend -or (Test-SmokeLinuxBackendStopped $backend $backend)) { return }
    try {
        $null = Invoke-RestMethod -Method Post -Uri ($Address.AbsoluteUri.TrimEnd('/') + '/api/v1/lifecycle/shutdown') -Headers @{ Authorization = 'Bearer ' + $Discovery.token } -TimeoutSec 10
    }
    catch {
        $observed = Get-SmokeLinuxBackendState $Discovery.processId
        Write-SmokeBackendState $Discovery.processId $observed 'after failed shutdown request'
        if (Test-SmokeLinuxBackendStopped $backend $observed) { return }
        # Killing the frontend tree may precede its backend's final state transition.
        # Only observed termination can forgive refusal; a live backend keeps the original error.
        if (Wait-SmokeLinuxBackendStopped $backend $ShutdownTimeoutMilliseconds) { return }
        throw
    }
    if (Wait-SmokeLinuxBackendStopped $backend $ShutdownTimeoutMilliseconds) { return }
    throw 'The smoke backend did not finish shutting down.'
}

function Stop-SmokeBackend([string]$DataDirectory) {
    $discoveryPath = Join-Path $DataDirectory 'backend/endpoint.json'
    if (-not (Test-Path -LiteralPath $discoveryPath)) { return }
    $discovery = Get-Content -LiteralPath $discoveryPath -Raw | ConvertFrom-Json
    $address = [uri]$discovery.address
    if ($address.Scheme -ne 'http' -or $address.Host -ne '127.0.0.1') {
        throw 'Smoke backend discovery is not a numeric loopback endpoint.'
    }
    if ($discovery.processId -le 0) { throw 'Smoke backend discovery has no valid process ID.' }
    if ($IsLinux) {
        # A missing procfs is not evidence that the selected backend exited.
        $null = [IO.File]::ReadAllText('/proc/self/stat')
        Stop-SmokeLinuxBackend $discovery $address
        return
    }
    $backend = Get-Process -Id $discovery.processId -ErrorAction SilentlyContinue
    if ($null -eq $backend) { return }
    $null = Invoke-RestMethod -Method Post -Uri ($address.AbsoluteUri.TrimEnd('/') + '/api/v1/lifecycle/shutdown') -Headers @{ Authorization = 'Bearer ' + $discovery.token } -TimeoutSec 10
    if (-not $backend.WaitForExit(30000)) { throw 'The smoke backend did not finish shutting down.' }
}
