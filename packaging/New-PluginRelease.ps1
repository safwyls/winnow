[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/PluginPackaging.ps1"
$release = & "$PSScriptRoot/Resolve-Version.ps1" -Version $Version
[xml]$versionProps = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'Version.props')
if ($release.Numeric -ne $versionProps.Project.PropertyGroup.VersionPrefix) { throw 'Release version must use the base in Version.props.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw 'Plugin release output must be empty so stale packages cannot enter a release.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($plugin in Get-FirstPartyPlugins) {
    & "$PSScriptRoot/New-PluginPackage.ps1" -PluginDirectory $plugin.Directory -OutputDirectory $output | Out-Host
}
New-PluginCatalog $output $Version | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath (Join-Path $output 'winnow-plugins.json') -Encoding utf8NoBOM
Assert-PluginCatalog $output $Version
Write-Output "Verified all three plugin packages and catalogue for v$Version."
