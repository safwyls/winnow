param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\xbox-plugin')
)

& (Join-Path $PSScriptRoot '../../packaging/New-PluginPackage.ps1') -PluginDirectory $PSScriptRoot -OutputDirectory $OutputDirectory
