[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$release = & "$PSScriptRoot/Resolve-Version.ps1" -Version $Version
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw 'Publish output must be empty so stale files cannot enter a release.'
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$buildOutput = Join-Path $repo "artifacts/build/$Runtime/"
& dotnet publish "$repo/src/Winnow.App/Winnow.App.csproj" --configuration Release --runtime $Runtime `
    --self-contained true --output $output `
    "-p:BaseOutputPath=$buildOutput" "-p:Version=$Version" `
    "-p:AssemblyVersion=$($release.Numeric).0" "-p:FileVersion=$($release.Numeric).0" `
    "-p:SourceRevisionId=$Commit" -p:PublishTrimmed=false -p:PublishSingleFile=false -warnaserror
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if (Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -eq 'appsettings.local.json' -or $_.Name -like '*.secrets.json' -or $_.Extension -eq '.db' }) {
    throw 'Publish output contains a local configuration, secret file, or database; refusing to package it.'
}

$binary = if ($Runtime -eq 'win-x64') { 'Winnow.exe' } else { 'Winnow' }
foreach ($required in @($binary, 'Winnow.dll', 'Winnow.runtimeconfig.json', 'Winnow.deps.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $output $required) -PathType Leaf)) { throw "Missing $required." }
}
$config = Get-Content -LiteralPath (Join-Path $output 'Winnow.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$config.runtimeOptions.includedFrameworks) { throw 'The published app is not self-contained.' }
@{ version = $Version; runtime = $Runtime; commit = $Commit.ToLowerInvariant() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding utf8NoBOM
