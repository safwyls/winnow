param([Parameter(Mandatory)][string]$ManifestPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workDirectory = Split-Path -Parent $ManifestPath
$payloadLock = $null

function Quote-NativeArgument([string]$value) {
    # CommandLineToArgvW escaping, including trailing backslashes inside quotes.
    return '"' + [regex]::Replace([regex]::Replace($value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}

try {
    $handoff = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $parent = Get-Process -Id $handoff.ProcessId -ErrorAction Stop
    if ($parent.StartTime.ToUniversalTime().Ticks.ToString() -ne $handoff.ProcessStartTicks -or
        $parent.Path -ine $handoff.Executable) { throw 'The requesting process changed.' }
    $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $registry.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2A9E417-5D4B-4B85-8738-7D6E993E51CE}_is1')
    try {
        if ($null -eq $key -or [IO.Path]::GetFullPath($key.GetValue('InstallLocation')).TrimEnd('\') -ine
            [IO.Path]::GetFullPath($handoff.InstallDirectory).TrimEnd('\')) { throw 'The installed app location changed.' }
    } finally { if ($null -ne $key) { $key.Dispose() }; $registry.Dispose() }
    if ([IO.Path]::GetFullPath($handoff.Executable) -ine (Join-Path $handoff.InstallDirectory 'Winnow.exe')) {
        throw 'Only the registered installed app can be updated.'
    }
    # Keep a read-only sharing handle until Setup exits to prevent replacement after verification.
    $payloadLock = [IO.File]::Open($handoff.Installer, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($hasher.ComputeHash($payloadLock)).Replace('-', '') }
    finally { $hasher.Dispose() }
    if ($handoff.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $digest -ine $handoff.Sha256) { throw 'Update checksum verification failed.' }
    [IO.File]::WriteAllText((Join-Path $workDirectory 'ready'), 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(120, [Math]::Max(1, $handoff.WaitSeconds)))
    while (-not $parent.HasExited -or -not (Test-Path -LiteralPath (Join-Path $workDirectory 'proceed'))) {
        if ((Test-Path -LiteralPath (Join-Path $workDirectory 'cancel')) -or [DateTime]::UtcNow -ge $deadline) {
            throw 'Update cancelled or Winnow did not finish shutting down. No installer was run.'
        }
        Start-Sleep -Milliseconds 100
        $parent.Refresh()
    }
    if (Test-Path -LiteralPath (Join-Path $workDirectory 'cancel')) { throw 'Update cancelled.' }
    # Refuse a second copy or locked binary; do not ask Restart Manager to close applications.
    Get-ChildItem -LiteralPath $handoff.InstallDirectory -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' } | ForEach-Object {
        $probe = [IO.File]::Open($_.FullName, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $probe.Dispose()
    }
    $setupArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOCLOSEAPPLICATIONS', '/NOFORCECLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', '/RESTARTEXITCODE=3010',
        ('/DIR=' + (Quote-NativeArgument $handoff.InstallDirectory)), ('/LOG=' + (Quote-NativeArgument (Join-Path $workDirectory 'installer.log'))))
    $setup = Start-Process -FilePath $handoff.Installer -ArgumentList $setupArgs -WindowStyle Hidden -PassThru -Wait
    if ($setup.ExitCode -ne 0) { throw "Installer returned $($setup.ExitCode). Run the latest official installer manually in the existing install directory; restart Windows first if code 3010." }
    $restartArgs = @($handoff.Arguments | ForEach-Object { Quote-NativeArgument $_ })
    $restarted = Start-Process -FilePath $handoff.Executable -ArgumentList $restartArgs -WorkingDirectory $handoff.InstallDirectory -WindowStyle Hidden -PassThru
    [IO.File]::WriteAllText((Join-Path $workDirectory 'restarted.json'), ($restarted.Id | ConvertTo-Json))
    Start-Sleep -Seconds 3
    if ($restarted.HasExited) { throw "Winnow exited after restart with code $($restarted.ExitCode). Start it manually and inspect its startup log." }
    [IO.File]::WriteAllText((Join-Path $workDirectory 'complete'), 'Update installed and Winnow restarted.')
} catch {
    $message = $_.Exception.Message + "`r`nRecovery: download the latest Winnow installer from the official GitHub Releases page and install into the existing directory. Keep the data directory. Do not restore older binaries over a migrated database. Installer details are in installer.log beside this file."
    [IO.File]::WriteAllText((Join-Path $workDirectory 'failure.txt'), $message)
    exit 1
} finally { if ($null -ne $payloadLock) { $payloadLock.Dispose() } }
