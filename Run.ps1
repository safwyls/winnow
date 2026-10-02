[CmdletBinding()]
param(
    [switch]$Preview,
    [string]$DataDirectory,
    [switch]$SeedSample,
    [switch]$NoSync,
    [string[]]$ApplicationArguments = @(),
    [string]$ArtifactsPath = (Join-Path $PSScriptRoot '.tmp/development-artifacts')
)

$ErrorActionPreference = 'Stop'
$artifacts = [IO.Path]::GetFullPath($ArtifactsPath)
& "$PSScriptRoot/Build.ps1" -Configuration Debug -ArtifactsPath $artifacts -CompanionsOnly:(-not $Preview)
$suffix = if ($IsWindows) { '.exe' } else { '' }
$backend = Join-Path $artifacts "bin/Winnow.Backend/debug/Winnow.Backend$suffix"
$helper = Join-Path $artifacts "bin/Winnow.Update.Helper/debug/Winnow.Update.Helper$suffix"
foreach ($path in @($backend, $helper)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The companion build did not produce $path." }
}
$arguments = @()
if ($DataDirectory) { $arguments += @('--data-dir', [IO.Path]::GetFullPath($DataDirectory)) }
if ($SeedSample) { $arguments += '--seed-sample' }
if ($NoSync) { $arguments += '--no-sync' }
$arguments += $ApplicationArguments
$previous = @{}
foreach ($name in @('WINNOW_BACKEND_PATH', 'WINNOW_ACTIVATION_HELPER_PATH', 'WINNOW_UPDATE_HELPER_PATH')) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:WINNOW_BACKEND_PATH = $backend
    $env:WINNOW_ACTIVATION_HELPER_PATH = $backend
    $env:WINNOW_UPDATE_HELPER_PATH = $helper
    Push-Location (Join-Path $PSScriptRoot 'src/Winnow.Electron')
    try {
        $viteArguments = if ($Preview) { @('preview', '--skipBuild', '--') } else { @('dev', '--') }
        & node node_modules/electron-vite/bin/electron-vite.js @viteArguments @arguments
        if ($LASTEXITCODE -ne 0) { throw "Electron exited with code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
