[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Stop-SmokeBackend.ps1')
$script:checks = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Assert-Failure([scriptblock]$Action, [string]$Message) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception }
    Assert-True ($null -ne $failure -and $failure.Message -ceq $Message) "Expected preserved failure: $Message"
}
function New-State([string]$State = 'S', [ulong]$Birth = 8123, [int]$ProcessId = 7280) {
    [pscustomobject]@{ processId = $ProcessId; startTicks = $Birth; state = $State }
}

if ($IsLinux) {
    $first = Get-SmokeLinuxBackendState $PID
    $second = Get-SmokeLinuxBackendState $PID
    Assert-True ($first.processId -eq $PID -and $first.startTicks -eq $second.startTicks -and
        $first.state -cnotin @('Z', 'X')) 'The actual Linux self-process identity is inconsistent.'
}
foreach ($status in @('R', 'S', 'D', 'T', 't', 'I', 'Z', 'X')) {
    $fields = @($status, '17') + @('0') * 17 + @('8123', '0')
    $parsed = ConvertFrom-PortableProcStat ('7280 (backend (worker)) ' + ($fields -join ' ')) ''
    Assert-True ($parsed.state -ceq $status -and $parsed.startTicks -eq 8123) 'Proc state or exact birth was lost.'
    Assert-True ((Test-SmokeLinuxBackendStopped $parsed $parsed) -eq ($status -cin @('Z', 'X'))) 'A live or stopped state was treated as terminated.'
}
Assert-Failure { ConvertFrom-PortableProcStat '7280 (backend) ?? 17 0' '' } 'Linux process stat has no exact parent/start identity.'

$script:states = [Collections.Generic.Queue[object]]::new()
$script:httpFailure = $false
$script:httpCalls = 0
$script:readerFailure = $null
$script:lastState = $null
function Get-SmokeLinuxBackendState([int]$ProcessId) {
    if ($null -ne $script:readerFailure) { throw $script:readerFailure }
    if ($script:states.Count -gt 0) { $script:lastState = $script:states.Dequeue() }
    return $script:lastState
}
function Invoke-RestMethod($Method, $Uri, $Headers, $TimeoutSec) {
    $script:httpCalls++
    Assert-True ($Method -ceq 'Post' -and $Uri -ceq 'http://127.0.0.1:12345/api/v1/lifecycle/shutdown' -and
        $Headers.Authorization -ceq 'Bearer fake-smoke-token' -and $TimeoutSec -eq 10) 'The real shutdown request contract changed.'
    if ($script:httpFailure) { throw [InvalidOperationException]::new('Controlled HTTP refusal.') }
}
function Set-States([object[]]$States, [bool]$HttpFailure = $false) {
    $script:states.Clear()
    foreach ($state in $States) { $script:states.Enqueue($state) }
    $script:lastState = $null
    $script:httpFailure = $HttpFailure
    $script:httpCalls = 0
    $script:readerFailure = $null
}
$discovery = [pscustomobject]@{ processId = 7280; token = 'fake-smoke-token' }
$address = [uri]'http://127.0.0.1:12345'
foreach ($initial in @($null, (New-State 'Z'), (New-State 'X'))) {
    Set-States @($initial)
    Stop-SmokeLinuxBackend $discovery $address
    Assert-True ($script:httpCalls -eq 0) 'An absent or terminated backend received an HTTP request.'
}
foreach ($final in @($null, (New-State 'Z'), (New-State 'X'))) {
    foreach ($refusal in @($false, $true)) {
        Set-States @((New-State), $final) $refusal
        Stop-SmokeLinuxBackend $discovery $address
        Assert-True ($script:httpCalls -eq 1) 'A live backend did not receive exactly one authenticated shutdown request.'
    }
}
Set-States @((New-State), (New-State)) $true
Assert-Failure { Stop-SmokeLinuxBackend $discovery $address -ShutdownTimeoutMilliseconds 1 } 'Controlled HTTP refusal.'
Set-States @((New-State), (New-State), (New-State), (New-State 'Z')) $true
Stop-SmokeLinuxBackend $discovery $address -ShutdownTimeoutMilliseconds 100
Assert-True ($script:httpCalls -eq 1 -and $script:states.Count -eq 0) 'Refused HTTP did not wait for exact backend termination.'
foreach ($refusal in @($false, $true)) {
    foreach ($replacement in @((New-State 'Z' 8124), (New-State 'S' 8124), (New-State 'Z' 8123 7281))) {
        Set-States @((New-State), $replacement) $refusal
        Assert-Failure { Stop-SmokeLinuxBackend $discovery $address } 'The smoke backend PID was reused; refusing to treat another process as the backend.'
    }
}
Set-States @((New-State 'T'))
Assert-Failure { Stop-SmokeLinuxBackend $discovery $address -ShutdownTimeoutMilliseconds 1 } 'The smoke backend did not finish shutting down.'
Set-States @()
$script:readerFailure = [UnauthorizedAccessException]::new('Controlled procfs access refusal.')
Assert-Failure { Stop-SmokeLinuxBackend $discovery $address } 'Controlled procfs access refusal.'
Assert-True ($script:httpCalls -eq 0) 'Unreadable procfs was treated as a live or absent backend.'

$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Stop-SmokeBackend.ps1'), [ref]$null, [ref]$errors)
Assert-True ($errors.Count -eq 0) 'Backend cleanup does not parse.'
Assert-True ($ast.Extent.Text.Contains('$backend.WaitForExit(30000)')) 'The Windows shutdown deadline changed.'
Assert-True ($ast.Extent.Text.Contains('[int]$ShutdownTimeoutMilliseconds = 30000')) 'The Linux shutdown deadline changed.'
Write-Output "Passed $script:checks backend shutdown contracts; no process was launched or stopped and no network request was made."
