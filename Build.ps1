[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ArtifactsPath = (Join-Path $PSScriptRoot '.tmp/development-artifacts'),
    [switch]$CompanionsOnly
)

$ErrorActionPreference = 'Stop'
$artifacts = [IO.Path]::GetFullPath($ArtifactsPath)
foreach ($project in @('Winnow.Backend', 'Winnow.Update.Helper')) {
    & dotnet build (Join-Path $PSScriptRoot "src/$project/$project.csproj") --configuration $Configuration --artifacts-path $artifacts
    if ($LASTEXITCODE -ne 0) { throw "$project build failed ($LASTEXITCODE)." }
}
if (-not $CompanionsOnly) {
    Push-Location (Join-Path $PSScriptRoot 'src/Winnow.Electron')
    try {
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "Electron build failed ($LASTEXITCODE). Run npm ci in src/Winnow.Electron before the first build." }
    }
    finally { Pop-Location }
}
