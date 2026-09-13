[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows file sharing semantics are required.' }

# Load only the lock check; never execute the installer helper's entry point.
$parseErrors = $null
$source = Join-Path $PSScriptRoot '../../src/Winnow.App/Services/Install-Update.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Wait-UpdateBinariesUnlocked' }, $true)
if (-not $function) { throw 'Binary lock check was not found.' }
Invoke-Expression $function.Extent.Text

$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-lock-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$binary = Join-Path $root 'fixture.dll'
$cancel = Join-Path $root 'cancel'
$ready = Join-Path $root 'ready'
$child = $null
$held = $null
function Start-LockFixture([string]$Code) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Arguments = '-NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Code))
    return [Diagnostics.Process]::Start($start)
}
try {
    [IO.File]::WriteAllText($binary, 'lock fixture')
    Wait-UpdateBinariesUnlocked $root $cancel 200
    Write-Host 'Writable binaries pass without waiting for a lock.'
    $child = Start-LockFixture ('$lock=[IO.File]::Open(''' + $binary.Replace("'", "''") + ''',[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read); [IO.File]::WriteAllText(''' + $ready.Replace("'", "''") + ''',''ready''); Start-Sleep -Milliseconds 700; $lock.Dispose()')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($child.HasExited -or $timer.Elapsed.TotalSeconds -gt 10) { throw 'Lock fixture did not become ready.' }
        Start-Sleep -Milliseconds 20
    }
    Wait-UpdateBinariesUnlocked $root $cancel 3000
    if (-not $child.WaitForExit(10000) -or $child.ExitCode -ne 0) { throw 'Lock fixture failed.' }
    Write-Host 'Released sharing lock permits the binary check.'

    $held = [IO.File]::Open($binary, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $refused = $false
    $timer.Restart()
    try { Wait-UpdateBinariesUnlocked $root $cancel 200 } catch [IO.IOException] { $refused = $true }
    if (-not $refused -or $timer.Elapsed.TotalSeconds -gt 3) { throw 'Persistent lock was not refused within the bound.' }
    Write-Host 'Persistent sharing lock prevents installation.'

    $child = Start-LockFixture ('Start-Sleep -Milliseconds 300; [IO.File]::WriteAllText(''' + $cancel.Replace("'", "''") + ''',''cancel'')')
    $cancelled = $false
    try { Wait-UpdateBinariesUnlocked $root $cancel 3000 } catch { if ($_.Exception.Message -ne 'Update cancelled.') { throw }; $cancelled = $true }
    if (-not $cancelled) { throw 'Cancellation during lock wait was ignored.' }
    Write-Host 'Cancellation interrupts the sharing-lock wait.'
} finally {
    if ($held) { $held.Dispose() }
    if ($child -and -not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
    $resolved = [IO.Path]::GetFullPath($root)
    $prefix = Join-Path ([IO.Path]::GetTempPath()) 'Winnow-lock-test-'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $resolved -notmatch 'Winnow-lock-test-[0-9a-f]{32}$') { throw 'Fixture cleanup escaped its temporary directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
