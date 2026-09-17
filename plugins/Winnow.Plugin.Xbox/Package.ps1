param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\xbox-plugin')
)

$ErrorActionPreference = 'Stop'
$packageOutput = [IO.Path]::GetFullPath($OutputDirectory)
$stageDirectory = Join-Path $packageOutput ('stage-' + [Guid]::NewGuid().ToString('N'))
$buildDirectory = Join-Path $packageOutput 'build'
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

dotnet publish (Join-Path $PSScriptRoot 'Winnow.Plugin.Xbox.csproj') -c Release --artifacts-path $buildDirectory -o $stageDirectory
if ($LASTEXITCODE -ne 0) { throw 'Xbox plugin publish failed.' }

$manifest = Get-Content -LiteralPath (Join-Path $stageDirectory 'plugin.json') -Raw | ConvertFrom-Json
$packagePath = Join-Path $packageOutput ('Winnow.Plugin.Xbox-' + $manifest.version + '.zip')
$packageFiles = @(
    (Join-Path $stageDirectory 'Winnow.Plugin.Xbox.dll'),
    (Join-Path $stageDirectory 'Winnow.Plugin.Xbox.deps.json'),
    (Join-Path $stageDirectory 'plugin.json'),
    (Join-Path $PSScriptRoot 'README.md')
)
# The host supplies Winnow.PluginSdk. Do not package a second SDK assembly.
Compress-Archive -LiteralPath $packageFiles -DestinationPath $packagePath -Force
Write-Output $packagePath
