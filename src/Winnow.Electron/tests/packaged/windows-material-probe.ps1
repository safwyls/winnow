<#
Captures only an owned, foreground Winnow window with a controlled underlay.
Run with Windows PowerShell 5.1 for its inbox WinForms/System.Drawing assemblies.
The JSON records DWM configuration separately from the screen-composited PNG.
#>
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Capture')][string]$WindowHandle,
    [Parameter(Mandatory, ParameterSetName = 'Capture')][ValidateRange(1, 2147483647)][int]$ProcessId,
    [Parameter(Mandatory, ParameterSetName = 'Capture')][string]$OutputPath,
    [Parameter(ParameterSetName = 'Capture')][ValidateSet(0, 1)][int]$Pattern = 0,
    [Parameter(ParameterSetName = 'Capture')][switch]$ClientCenter,
    [Parameter(ParameterSetName = 'Compile')][switch]$CompileOnly
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Use Windows PowerShell (powershell.exe), not pwsh, for this inbox WinForms probe.'
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'windows-material-probe.cs') -ReferencedAssemblies System.Windows.Forms, System.Drawing
if ($CompileOnly) {
    'Windows material capture helper compiled; no windows opened and no screen captured.'
    return
}
if (-not [IO.Path]::IsPathRooted($OutputPath) -or [IO.Path]::GetExtension($OutputPath) -ne '.png') {
    throw 'OutputPath must be an absolute .png path.'
}
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$reportPath = [IO.Path]::ChangeExtension($outputFullPath, '.json')
if ((Test-Path -LiteralPath $outputFullPath) -or (Test-Path -LiteralPath $reportPath)) {
    throw 'Use a new output path; evidence is never overwritten.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
$handleValue = if ($WindowHandle.StartsWith('0x', [StringComparison]::OrdinalIgnoreCase)) {
    [Convert]::ToInt64($WindowHandle.Substring(2), 16)
} else {
    [long]::Parse($WindowHandle, [Globalization.CultureInfo]::InvariantCulture)
}
try {
    $result = [WinnowMaterialProbe.Capture]::Run([IntPtr]::new($handleValue), $ProcessId, $outputFullPath, $Pattern, $ClientCenter.IsPresent)
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 8
} catch {
    [ordered]@{
        captured = $false
        targetHandle = $WindowHandle
        targetProcessId = $ProcessId
        utc = [DateTime]::UtcNow.ToString('O')
        error = $_.Exception.GetBaseException().Message
    } | ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding UTF8
    throw
}
