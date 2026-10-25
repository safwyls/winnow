# Native window checks used by disposable package smoke tests, never installer entry points.
if (-not ('WinnowSmokeWindow' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinnowSmokeWindow {
    private delegate bool Enumerate(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerate callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out UIntPtr result);
    public sealed class Window {
        public long Handle { get; set; }
        public string Title { get; set; }
        public string Class { get; set; }
        public bool Visible { get; set; }
        public bool Responding { get; set; }
    }
    public static Window[] Snapshot(uint process) {
        var windows = new List<Window>();
        EnumWindows((window, state) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != process) return true;
            var title = new StringBuilder(512);
            var kind = new StringBuilder(128);
            GetWindowText(window, title, title.Capacity);
            GetClassName(window, kind, kind.Capacity);
            windows.Add(new Window { Handle = window.ToInt64(), Title = title.ToString(), Class = kind.ToString(),
                Visible = IsWindowVisible(window), Responding = SendMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, 2, 250, out _) != IntPtr.Zero });
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
    public static bool Close(uint process, long handle) {
        var window = new IntPtr(handle);
        GetWindowThreadProcessId(window, out uint owner);
        return owner == process && PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }
}
'@
}

function Close-SmokeApplication {
    param(
        [Parameter(Mandatory)]$Process,
        [Parameter(Mandatory)][string]$Report,
        [string]$DataDirectory,
        [string]$BackendExecutable,
        [switch]$Electron,
        [ValidateRange(1, 120)][int]$TimeoutSeconds = 60
    )
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $result = [ordered]@{
        processId = $Process.Id; processStartTicks = $Process.StartTime.ToUniversalTime().Ticks.ToString()
        executable = $Process.Path; ready = $false; closeSent = $false; exitCode = $null
        elapsedMilliseconds = 0; windows = @(); backendProcessId = $null; backendHealthy = $false
        backendFailureType = $null; failure = $null
    }
    try {
        while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            $Process.Refresh()
            if ($Process.HasExited) {
                $result.exitCode = $Process.ExitCode
                throw "Winnow exited before its application window was ready (code $($Process.ExitCode))."
            }
            $result.windows = @([WinnowSmokeWindow]::Snapshot($Process.Id))
            # A native error message box is not a healthy application window. Electron
            # must have shown its own responsive top-level window after startup.
            $windows = @($result.windows | Where-Object {
                $_.Responding -and $_.Class -ne '#32770' -and $_.Title.StartsWith('Winnow', [StringComparison]::Ordinal) -and
                (-not $Electron -or ($_.Visible -and $_.Class -eq 'Chrome_WidgetWin_1'))
            })
            if ($Electron -and -not $result.backendHealthy) {
                $endpointPath = Join-Path $DataDirectory 'backend/endpoint.json'
                if (Test-Path -LiteralPath $endpointPath -PathType Leaf) {
                    try {
                        $endpoint = Get-Content -LiteralPath $endpointPath -Raw | ConvertFrom-Json
                        $address = [uri]$endpoint.address
                        if ($address.Scheme -ne 'http' -or $address.Host -ne '127.0.0.1') { throw 'Invalid smoke backend address.' }
                        $backend = Get-Process -Id $endpoint.processId -ErrorAction Stop
                        if ($backend.Path -ine $BackendExecutable) { throw 'The restarted frontend did not use its installed backend.' }
                        $health = Invoke-RestMethod -Uri ($address.AbsoluteUri.TrimEnd('/') + '/api/v1/health') -Headers @{ Authorization = 'Bearer ' + $endpoint.token } -TimeoutSec 2
                        if ($health.epoch -ne $endpoint.epoch) { throw 'The smoke backend epoch changed.' }
                        $result.backendProcessId = $backend.Id
                        $result.backendHealthy = $true
                    } catch {
                        # Discovery is replaced during startup. Retry until this bounded
                        # readiness deadline; no credential or arbitrary response is logged.
                        $result.backendFailureType = $_.Exception.GetType().FullName
                    }
                }
            }
            if ($windows.Count -gt 0 -and (-not $Electron -or $result.backendHealthy)) {
                $result.ready = $true
                $result.closeSent = [WinnowSmokeWindow]::Close($Process.Id, $windows[0].Handle)
                if (-not $result.closeSent) { throw "The ready Winnow window refused a close request for process $($Process.Id)." }
                return
            }
            Start-Sleep -Milliseconds 100
        }
        throw "Winnow did not expose a responsive application window and required backend within $TimeoutSeconds seconds."
    } catch {
        $result.failure = $_.Exception.Message
        throw
    } finally {
        $result.elapsedMilliseconds = $clock.ElapsedMilliseconds
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Report) -Force
        $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding utf8
    }
}
