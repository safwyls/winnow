[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/PluginPackaging.ps1"
Assert-PluginCatalog ([IO.Path]::GetFullPath($PackageDirectory)) $Version
Write-Output "Verified plugin release assets for v$Version."
