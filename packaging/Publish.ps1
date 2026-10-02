[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$ExtraProperties = @()
)

$ErrorActionPreference = 'Stop'
# The explicit Publish-Avalonia.ps1 entry point retains the reference frontend.
& "$PSScriptRoot/Publish-Electron.ps1" @PSBoundParameters
