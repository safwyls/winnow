param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\psn-plugin')
)

& (Join-Path $PSScriptRoot '../../packaging/New-PluginPackage.ps1') -PluginDirectory $PSScriptRoot -OutputDirectory $OutputDirectory
