$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-release-selection-' + [guid]::NewGuid().ToString('N'))
$fixtureFile = Join-Path $fixtureRoot 'setup.exe'
$fixtureBytes = [Text.Encoding]::UTF8.GetBytes('release selection fixture')
$fixtureHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fixtureBytes))

# Invoke-RestMethod returns a JSON array as one pipeline object. Reproduce that
# shape so wrapping its result in @() cannot silently hide all release candidates.
function Invoke-RestMethod {
    param($Uri, $Headers)
    Write-Output -NoEnumerate @(
        @{ tag_name = 'v0.1.0-beta.4'; draft = $false; assets = @(@{
            name = 'Winnow-0.1.0-beta.4-win-x64-setup.exe'
            browser_download_url = 'https://github.com/safwyls/winnow/releases/download/v0.1.0-beta.4/Winnow-0.1.0-beta.4-win-x64-setup.exe'
            digest = "sha256:$fixtureHash"
        }) },
        @{ tag_name = 'v0.1.0-beta.5'; draft = $false; assets = @(@{
            name = 'Winnow-0.1.0-beta.5-win-x64-setup.exe'
            browser_download_url = 'https://github.com/safwyls/winnow/releases/download/v0.1.0-beta.5/Winnow-0.1.0-beta.5-win-x64-setup.exe'
            digest = "sha256:$fixtureHash"
        }) }
    )
}
function Invoke-WebRequest {
    param($Uri, $OutFile)
    if (-not $Uri.ToString().EndsWith('Winnow-0.1.0-beta.5-win-x64-setup.exe')) { throw 'Wrong baseline selected.' }
    [IO.File]::WriteAllBytes($OutFile, $fixtureBytes)
}
try {
    & "$PSScriptRoot/Get-PreviousWindowsInstaller.ps1" -Version '0.1.0-ci.35' -OutputPath $fixtureFile
    if (-not (Test-Path -LiteralPath $fixtureFile)) { throw 'No baseline was downloaded.' }
    Write-Host 'Previous Windows installer selection passed.'
} finally {
    if (Test-Path -LiteralPath $fixtureFile) { Remove-Item -LiteralPath $fixtureFile }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot }
}
