$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-release-selection-' + [guid]::NewGuid().ToString('N'))
$fixtureFile = Join-Path $fixtureRoot 'setup.exe'
$fixtureBytes = [Text.Encoding]::UTF8.GetBytes('release selection fixture')
$fixtureHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fixtureBytes))
$fixtureSize = $fixtureBytes.Length

# Invoke-RestMethod returns a JSON array as one pipeline object. Reproduce that
# shape so wrapping its result in @() cannot silently hide all release candidates.
function Invoke-RestMethod {
    param($Uri, $Headers)
    Write-Output -NoEnumerate @(
        @{ tag_name = 'v0.1.0-beta.4'; draft = $false; assets = @(@{
            name = 'Winnow-0.1.0-beta.4-win-x64-setup.exe'
            browser_download_url = 'https://github.com/safwyls/winnow/releases/download/v0.1.0-beta.4/Winnow-0.1.0-beta.4-win-x64-setup.exe'
            digest = "sha256:$fixtureHash"
            size = $fixtureSize
        }) },
        @{ tag_name = 'v0.1.0-beta.5'; draft = $false; assets = @(@{
            name = 'Winnow-0.1.0-beta.5-win-x64-setup.exe'
            browser_download_url = 'https://github.com/safwyls/winnow/releases/download/v0.1.0-beta.5/Winnow-0.1.0-beta.5-win-x64-setup.exe'
            digest = "sha256:$fixtureHash"
            size = $fixtureSize
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
    $evidencePath = "$fixtureFile.evidence.json"
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    if ($evidence.previousVersion -cne '0.1.0-beta.5' -or $evidence.targetVersion -cne '0.1.0-ci.35' -or
        $evidence.runtime -cne 'win-x64' -or $evidence.sha256 -cne $fixtureHash -or $evidence.size -ne $fixtureBytes.Length) {
        throw 'Baseline evidence did not record the exact verified release, digest and size.'
    }
    Remove-Item -LiteralPath $evidencePath
    $fixtureSize++
    $refused = $false
    try { & "$PSScriptRoot/Get-PreviousWindowsInstaller.ps1" -Version '0.1.0-ci.35' -OutputPath $fixtureFile }
    catch {
        if ($_.Exception.Message -cne 'Previous installer size or checksum does not match GitHub.') { throw }
        $refused = $true
    }
    if (-not $refused -or (Test-Path -LiteralPath $evidencePath)) { throw 'A mismatched asset size was accepted or emitted passing evidence.' }
    Write-Host 'Previous Windows installer selection, exact evidence and wrong-size refusal passed.'
} finally {
    if (Test-Path -LiteralPath $fixtureFile) { Remove-Item -LiteralPath $fixtureFile }
    if (Test-Path -LiteralPath "$fixtureFile.evidence.json") { Remove-Item -LiteralPath "$fixtureFile.evidence.json" }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot }
}
