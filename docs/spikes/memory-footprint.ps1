<#
.SYNOPSIS
  Measures Winnow's process memory and breaks it down by region type.
  Companion to docs/spikes/memory-footprint.md; the numbers there came from this.

.DESCRIPTION
  Launches an executable, waits, then records working set, private bytes, thread
  and handle counts, the GC heap sizes from dotnet-counters (when the tool is on
  PATH or in -ToolPath), and a VMMap-style breakdown of committed and resident
  memory by region type (Image / Mapped / Private), by page protection, and by
  allocation base. The process is stopped afterwards unless -KeepRunning is set.

  Always pass a throwaway -DataDir. The app writes to whatever directory it is
  given, and a run without --no-sync contacts the real launchers and web services.

.EXAMPLE
  ./docs/spikes/memory-footprint.ps1 -Exe .\artifacts\bin\Winnow.exe -DataDir C:\Temp\winnow-mem -Label real
  ./docs/spikes/memory-footprint.ps1 -Exe .\artifacts\bin\Winnow.exe -DataDir C:\Temp\winnow-mem -NoSync -Label real-nosync
  ./docs/spikes/memory-footprint.ps1 -Exe .\artifacts\bin\Winnow.exe -DataDir C:\Temp\winnow-mem -Env @{ DOTNET_GCConserveMemory = '9' }
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir,
  [switch] $NoSync,
  [string] $Label = 'run',
  [int[]] $SampleAtSeconds = @(30, 90),
  [hashtable] $Env = @{},
  [string] $ToolPath = '',
  [string] $OutDir = (Join-Path $env:TEMP 'winnow-memory'),
  [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

# --- region classifier -------------------------------------------------------
# VirtualQueryEx walks every committed region; QueryWorkingSetEx reports which
# of its pages are resident. Committed private bytes approximate Task Manager's
# "Commit"/private bytes; resident private bytes are the part of the working set
# that is neither a DLL image nor a shared section.
$vq = @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class VQ {
  [StructLayout(LayoutKind.Sequential)] public struct MBI { public IntPtr BaseAddress; public IntPtr AllocationBase; public uint AllocationProtect; public uint pad; public IntPtr RegionSize; public uint State; public uint Protect; public uint Type; }
  [StructLayout(LayoutKind.Sequential)] public struct WSEX { public IntPtr Va; public IntPtr Info; }
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, int len);
  [DllImport("psapi.dll", CharSet=CharSet.Unicode)] public static extern uint GetMappedFileNameW(IntPtr h, IntPtr addr, StringBuilder name, uint size);
  [DllImport("psapi.dll", SetLastError=true)] public static extern bool QueryWorkingSetEx(IntPtr h, [In,Out] WSEX[] pv, int cb);
  public static long ResidentBytes(IntPtr h, long baseAddr, long size) {
    long pages = size / 4096, resident = 0; int chunk = 16384;
    for (long off = 0; off < pages; off += chunk) {
      int n = (int)Math.Min(chunk, pages - off);
      var arr = new WSEX[n];
      for (int i = 0; i < n; i++) arr[i].Va = new IntPtr(baseAddr + (off + i) * 4096);
      if (!QueryWorkingSetEx(h, arr, n * 16)) return -1;
      for (int i = 0; i < n; i++) if ((arr[i].Info.ToInt64() & 1) != 0) resident++;
    }
    return resident * 4096;
  }
}
'@
if (-not ('VQ' -as [type])) { Add-Type -TypeDefinition $vq }

function Get-Regions([int] $ProcessId) {
  $h = [VQ]::OpenProcess(0x0410, $false, $ProcessId)
  if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($ProcessId) failed" }
  $addr = [IntPtr]::Zero; $mbi = New-Object VQ+MBI; $sz = [Runtime.InteropServices.Marshal]::SizeOf($mbi)
  $out = New-Object System.Collections.Generic.List[object]
  while ([VQ]::VirtualQueryEx($h, $addr, [ref]$mbi, $sz) -eq $sz) {
    if ($mbi.State -eq 0x1000) {
      $type = switch ($mbi.Type) { 0x1000000 {'Image'} 0x40000 {'Mapped'} 0x20000 {'Private'} default {'?'} }
      $name = ''
      if ($type -ne 'Private') { $sb = New-Object System.Text.StringBuilder 1024; [void][VQ]::GetMappedFileNameW($h, $mbi.BaseAddress, $sb, 1024); $name = [IO.Path]::GetFileName($sb.ToString()) }
      $resident = [VQ]::ResidentBytes($h, [int64]$mbi.BaseAddress, [int64]$mbi.RegionSize)
      $out.Add([pscustomobject]@{
        Base = '{0:X12}' -f [int64]$mbi.BaseAddress; AllocBase = '{0:X12}' -f [int64]$mbi.AllocationBase
        Type = $type; Protect = '{0:X}' -f $mbi.Protect; Name = $name
        SizeMB = [math]::Round([int64]$mbi.RegionSize / 1MB, 2); ResidentMB = [math]::Round($resident / 1MB, 2) })
    }
    $next = [int64]$mbi.BaseAddress + [int64]$mbi.RegionSize
    if ($next -ge 0x7FFFFFFFFFFF) { break }
    $addr = [IntPtr]$next
  }
  return $out
}

function Show-Regions($regions, [string] $label) {
  "== ${label}: committed / resident by region type (MB)"
  $regions | Group-Object Type | ForEach-Object { '  {0,-8} committed={1,8:N1}  resident={2,8:N1}  regions={3}' -f $_.Name, ($_.Group | Measure-Object SizeMB -Sum).Sum, ($_.Group | Measure-Object ResidentMB -Sum).Sum, $_.Count }
  "== private by page protection (4 = RW data and native heaps, 404 = GC heap bookkeeping, 104 = stack guard)"
  $regions | Where-Object Type -eq 'Private' | Group-Object Protect | Sort-Object { ($_.Group | Measure-Object SizeMB -Sum).Sum } -Descending | ForEach-Object { '  prot={0,-4} committed={1,8:N1}  resident={2,8:N1}  regions={3}' -f $_.Name, ($_.Group | Measure-Object SizeMB -Sum).Sum, ($_.Group | Measure-Object ResidentMB -Sum).Sum, $_.Count }
  "== private allocations by allocation base, top 20 by resident (multi-region '4,1' blocks are NT heap segments; large single blocks are GC regions)"
  $regions | Where-Object Type -eq 'Private' | Group-Object AllocBase | ForEach-Object { [pscustomobject]@{ AllocBase = $_.Name; CommittedMB = [math]::Round(($_.Group | Measure-Object SizeMB -Sum).Sum, 1); ResidentMB = [math]::Round(($_.Group | Measure-Object ResidentMB -Sum).Sum, 1); Regions = $_.Count; Prot = (($_.Group | Select-Object -ExpandProperty Protect -Unique) -join ',') } } | Sort-Object ResidentMB -Descending | Select-Object -First 20 | Format-Table -AutoSize | Out-String -Width 160
  "== image resident by module, top 15"
  $regions | Where-Object Type -eq 'Image' | Group-Object Name | ForEach-Object { [pscustomobject]@{ Module = $_.Name; CommittedMB = [math]::Round(($_.Group | Measure-Object SizeMB -Sum).Sum, 1); ResidentMB = [math]::Round(($_.Group | Measure-Object ResidentMB -Sum).Sum, 1) } } | Sort-Object ResidentMB -Descending | Select-Object -First 15 | Format-Table -AutoSize | Out-String -Width 160
  "== mapped resident, top 5 (the unnamed entry is mostly the runtime's double-mapped JIT code and loader heaps)"
  $regions | Where-Object Type -eq 'Mapped' | Group-Object Name | ForEach-Object { [pscustomobject]@{ Name = $_.Name; CommittedMB = [math]::Round(($_.Group | Measure-Object SizeMB -Sum).Sum, 1); ResidentMB = [math]::Round(($_.Group | Measure-Object ResidentMB -Sum).Sum, 1) } } | Sort-Object ResidentMB -Descending | Select-Object -First 5 | Format-Table -AutoSize | Out-String -Width 160
}

function Get-GcCommittedMB([int] $ProcessId) {
  $counters = if ($ToolPath) { Join-Path $ToolPath 'dotnet-counters.exe' } else { 'dotnet-counters' }
  if (-not (Get-Command $counters -ErrorAction SilentlyContinue)) { return $null }
  $csv = Join-Path $OutDir "counters-$Label-$ProcessId.csv"
  & $counters collect -p $ProcessId --refresh-interval 1 --format csv -o $csv --counters System.Runtime --duration 00:00:03 2>&1 | Out-Null
  if (-not (Test-Path $csv)) { return $null }
  $row = Import-Csv $csv | Where-Object { $_.'Counter Name' -like '*committed_size*' } | Select-Object -Last 1
  if ($row) { [math]::Round([double]$row.'Mean/Increment' / 1MB, 1) } else { $null }
}

# --- launch ------------------------------------------------------------------
$args = @()
if ($DataDir) { $args += @('--data-dir', $DataDir) }
if ($NoSync) { $args += '--no-sync' }
foreach ($k in $Env.Keys) { [Environment]::SetEnvironmentVariable($k, $Env[$k], 'Process') }
$p = if ($args.Count) { Start-Process -FilePath $Exe -ArgumentList $args -PassThru -WorkingDirectory (Split-Path $Exe) } else { Start-Process -FilePath $Exe -PassThru }
foreach ($k in $Env.Keys) { [Environment]::SetEnvironmentVariable($k, $null, 'Process') }

$elapsed = 0
foreach ($t in $SampleAtSeconds) {
  Start-Sleep -Seconds ($t - $elapsed); $elapsed = $t
  if ($p.HasExited) { "$Label exited with code $($p.ExitCode) before t=$t s"; break }
  $p.Refresh()
  $gc = Get-GcCommittedMB $p.Id
  '{0,-16} t={1,3}s  WS={2,6:N1} MB  Private={3,6:N1} MB  Threads={4,3}  Handles={5,5}  GC committed={6}' -f $Label, $t, ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB), $p.Threads.Count, $p.HandleCount, $(if ($null -ne $gc) { "$gc MB" } else { 'n/a (dotnet-counters not found)' })
}
if (-not $p.HasExited) {
  $regions = Get-Regions $p.Id
  $regions | Export-Csv (Join-Path $OutDir "regions-$Label.csv") -NoTypeInformation
  Show-Regions $regions $Label
  if (-not $KeepRunning) { Stop-Process -Id $p.Id -Force }
}
