[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [Parameter(Mandatory)][string]$OutputDirectory,
    # Extra `-p:Name=Value` MSBuild properties appended to the publish command,
    # for one-off comparisons (TASK-152.4's ReadyToRun/GC trials, for example)
    # without editing this script per run. Empty by default; adopted settings
    # are baked in below instead of relying on a caller to pass them.
    [string[]]$ExtraProperties = @()
)

$ErrorActionPreference = 'Stop'
$release = & "$PSScriptRoot/Resolve-Version.ps1" -Version $Version
$repo = Split-Path $PSScriptRoot -Parent
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $repo 'Version.props')
if ($release.Numeric -ne $versionProps.Project.PropertyGroup.VersionPrefix) {
    throw 'Package version must use the base declared in Version.props.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw 'Publish output must be empty so stale files cannot enter a release.'
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$buildOutput = Join-Path $repo "artifacts/build/$Runtime/"
# PublishReadyToRun, win-x64 only (TASK-152.4): on the same real library,
# `--no-sync`, two runs each, this dropped the runtime's double-mapped JIT
# code and loader heaps ("Mapped resident" in docs/spikes/memory-footprint.ps1's
# region breakdown) from ~47.7 MB to ~36.1 MB and cut warm launch-to-window
# time from ~2.0 s to ~1.0 s, for +23 MB of published package size. Composite
# R2R measured only ~2 MB more Mapped-resident saving for +28 MB of package
# size over plain R2R and was not adopted. Scoped to win-x64 because that is
# the platform measured here; linux-x64 R2R was not tried, so it keeps its
# prior behavior. See the Follow-up section of docs/spikes/memory-footprint.md.
$readyToRun = @()
if ($Runtime -eq 'win-x64') { $readyToRun = @('-p:PublishReadyToRun=true') }
& dotnet publish "$repo/src/Winnow.App/Winnow.App.csproj" --configuration Release --runtime $Runtime `
    --self-contained true --output $output `
    "-p:BaseOutputPath=$buildOutput" "-p:Version=$Version" `
    "-p:AssemblyVersion=$($release.Numeric).0" "-p:FileVersion=$($release.Numeric).0" `
    "-p:SourceRevisionId=$Commit" -p:PublishTrimmed=false -p:PublishSingleFile=false `
    -p:ContinuousIntegrationBuild=true `
    @readyToRun @ExtraProperties -warnaserror
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if (Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -eq 'appsettings.local.json' -or $_.Name -like '*.secrets.json' -or $_.Extension -eq '.db' }) {
    throw 'Publish output contains a local configuration, secret file, or database; refusing to package it.'
}

& "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $output

$binary = if ($Runtime -eq 'win-x64') { 'Winnow.exe' } else { 'Winnow' }
foreach ($required in @($binary, 'Winnow.dll', 'Winnow.runtimeconfig.json', 'Winnow.deps.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $output $required) -PathType Leaf)) { throw "Missing $required." }
}
$config = Get-Content -LiteralPath (Join-Path $output 'Winnow.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$config.runtimeOptions.includedFrameworks) { throw 'The published app is not self-contained.' }
$identity = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'Winnow.dll'))
if ($identity.ProductVersion -cne "$Version+$Commit" -or $identity.FileVersion -ne "$($release.Numeric).0") {
    throw "Published assembly identity does not match the package: $($identity.ProductVersion), $($identity.FileVersion)."
}
@{ version = $Version; runtime = $Runtime; commit = $Commit.ToLowerInvariant() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding utf8NoBOM
