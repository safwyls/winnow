[CmdletBinding()]
param([string]$ArtifactsPath = '.tmp/electron-ci-fixtures')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath } else { Join-Path $root $ArtifactsPath }))
foreach ($project in @('tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj', 'src/Winnow.Update.Helper/Winnow.Update.Helper.csproj')) {
    & dotnet build (Join-Path $root $project) --configuration Debug --artifacts-path $output --nologo -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "Electron test companion build failed: $project" }
}
$suffix = if ($IsWindows) { '.exe' } else { '' }
$values = [ordered]@{
    WINNOW_BACKEND_PATH = Join-Path $output "bin/Winnow.Backend/debug/Winnow.Backend$suffix"
    WINNOW_ACTIVATION_HELPER_PATH = Join-Path $output "bin/Winnow.Backend/debug/Winnow.Backend$suffix"
    WINNOW_ELECTRON_FIXTURE_PATH = Join-Path $output "bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures$suffix"
    WINNOW_UPDATE_HELPER_PATH = Join-Path $output "bin/Winnow.Update.Helper/debug/Winnow.Update.Helper$suffix"
}
foreach ($item in $values.GetEnumerator()) {
    if (!(Test-Path -LiteralPath $item.Value -PathType Leaf)) { throw "Missing test companion: $($item.Key)" }
    [Environment]::SetEnvironmentVariable($item.Key, $item.Value, 'Process')
    if ($env:GITHUB_ENV) { "$($item.Key)=$($item.Value)" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append }
}
$values
