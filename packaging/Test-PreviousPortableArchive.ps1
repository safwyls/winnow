$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-portable-selection-' + [guid]::NewGuid().ToString('N'))
$fixtureBytes = [Text.Encoding]::UTF8.GetBytes('portable release selection fixture')
$fixtureHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fixtureBytes))
$invalidDigest = $false
function Invoke-RestMethod {
    param($Uri, $Headers)
    $releases = foreach ($version in @('0.1.0-beta.4', '0.1.0-beta.5', '0.1.0-ci.35')) {
        $assets = foreach ($suffix in @('win-x64.zip', 'linux-x64.tar.gz')) {
            $name = "Winnow-$version-$suffix"
            @{ name = $name; browser_download_url = "https://github.com/safwyls/winnow/releases/download/v$version/$name";
               digest = if ($invalidDigest) { 'sha256:invalid' } else { "sha256:$fixtureHash" }; size = $fixtureBytes.Length }
        }
        @{ tag_name = "v$version"; draft = $false; assets = @($assets) }
    }
    Write-Output -NoEnumerate @($releases)
}
function Invoke-WebRequest {
    param($Uri, $OutFile)
    if (-not $Uri.ToString().Contains('/v0.1.0-beta.5/')) { throw 'Wrong baseline selected (must be strictly older).' }
    [IO.File]::WriteAllBytes($OutFile, $fixtureBytes)
}
try {
    foreach ($runtime in @('win-x64', 'linux-x64')) {
        $file = Join-Path $fixtureRoot $runtime
        & "$PSScriptRoot/Get-PreviousPortableArchive.ps1" -Version '0.1.0-ci.35' -Runtime $runtime -OutputPath $file
        $evidence = Get-Content -LiteralPath "$file.evidence.json" -Raw | ConvertFrom-Json
        if ($evidence.previousVersion -cne '0.1.0-beta.5') { throw 'Incorrect previous-version evidence.' }
    }
    $invalidDigest = $true
    $rejected = $false
    try { & "$PSScriptRoot/Get-PreviousPortableArchive.ps1" -Version '0.1.0-ci.35' -Runtime win-x64 -OutputPath (Join-Path $fixtureRoot 'invalid') }
    catch { if ($_.Exception.Message -notlike '*no valid official URL or GitHub SHA-256 digest*') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'Invalid baseline digest was accepted.' }
    Write-Host 'Previous portable archive selection and digest rejection passed on both runtimes.'
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Get-ChildItem -LiteralPath $fixtureRoot -File | Remove-Item -Force
        Remove-Item -LiteralPath $fixtureRoot
    }
}
