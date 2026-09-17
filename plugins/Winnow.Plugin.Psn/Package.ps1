param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\psn-plugin')
)

$ErrorActionPreference = 'Stop'
$packageOutput = [IO.Path]::GetFullPath($OutputDirectory)
$stageDirectory = Join-Path $packageOutput ('stage-' + [Guid]::NewGuid().ToString('N'))
$buildDirectory = Join-Path $packageOutput 'build'
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

dotnet publish (Join-Path $PSScriptRoot 'Winnow.Plugin.Psn.csproj') -c Release --artifacts-path $buildDirectory -o $stageDirectory
if ($LASTEXITCODE -ne 0) { throw 'PlayStation plugin publish failed.' }

$manifest = Get-Content -LiteralPath (Join-Path $stageDirectory 'plugin.json') -Raw | ConvertFrom-Json
$packagePath = Join-Path $packageOutput ('Winnow.Plugin.Psn-' + $manifest.version + '.zip')
$packageFiles = @(
    (Join-Path $stageDirectory 'Winnow.Plugin.Psn.dll'),
    (Join-Path $stageDirectory 'Winnow.Plugin.Psn.deps.json'),
    (Join-Path $stageDirectory 'plugin.json'),
    (Join-Path $PSScriptRoot 'README.md')
)
# The host supplies Winnow.PluginSdk. Do not package a second SDK assembly.
Compress-Archive -LiteralPath $packageFiles -DestinationPath $packagePath -Force
Write-Output $packagePath
