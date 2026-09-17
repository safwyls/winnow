[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PluginDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/PluginPackaging.ps1"
$plugin = @(Get-FirstPartyPlugins | Where-Object { [IO.Path]::GetFullPath($_.Directory) -eq [IO.Path]::GetFullPath($PluginDirectory) })
if ($plugin.Count -ne 1) { throw 'Choose one of the first-party plugin directories.' }
$sourceManifest = Join-Path $plugin[0].Directory 'plugin.json'
$manifest = Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
$version = & "$PSScriptRoot/Resolve-Version.ps1" -Version $manifest.version
if ($version.Prerelease) { throw 'Plugin versions must use three numeric components.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$scratch = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('winnow-plugin-build-' + [Guid]::NewGuid().ToString('N'))))
$stage = Join-Path $scratch 'publish'
try {
    & dotnet publish (Join-Path $plugin[0].Directory ($plugin[0].Project + '.csproj')) --configuration Release `
        --artifacts-path (Join-Path $scratch 'build') --output $stage --self-contained false `
        "-p:Version=$($manifest.version)" "-p:AssemblyVersion=$($version.Numeric).0" "-p:FileVersion=$($version.Numeric).0" `
        -p:ContinuousIntegrationBuild=true -p:PublishTrimmed=false -p:PublishSingleFile=false -warnaserror | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed: $($manifest.id)." }
    $package = Join-Path $output ($plugin[0].Project + '-' + $manifest.version + '.zip')
    # The host supplies the SDK. An explicit file list excludes host binaries, settings and build output.
    $files = @(
        (Join-Path $stage $manifest.entryAssembly),
        (Join-Path $stage ($plugin[0].Project + '.deps.json')),
        (Join-Path $stage 'plugin.json'),
        (Join-Path $plugin[0].Directory 'README.md')
    )
    Compress-Archive -LiteralPath $files -DestinationPath $package -Force
    Read-PluginPackageRecord $package $sourceManifest | Out-Null
    Write-Output $package
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$scratch.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($scratch) -notlike 'winnow-plugin-build-*') { throw 'Unsafe plugin build cleanup path.' }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
