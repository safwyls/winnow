$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-Debian-selection-' + [guid]::NewGuid().ToString('N'))
$fixtureFile = Join-Path $fixtureRoot 'Winnow.deb'
$fixtureBytes = [Text.Encoding]::UTF8.GetBytes('Debian release selection fixture')
$fixtureHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fixtureBytes)).ToLowerInvariant()
$fixtureCase = 'valid'
$downloadState = @{ Count = 0 }
function Invoke-RestMethod {
    param($Uri, $Headers)
    $assets = foreach ($version in @('0.2.0-beta.2', '0.2.0-beta.3', '0.2.0-ci.9', '0.2.1')) {
        $name = "Winnow-$version-linux-x64.deb"
        @{ tag_name = "v$version"; draft = ($version -eq '0.2.1'); assets = @(@{
            name = $name
            browser_download_url = if ($fixtureCase -eq 'url') { "https://example.com/$name" } else { "https://github.com/safwyls/winnow/releases/download/v$version/$name" }
            digest = if ($fixtureCase -eq 'digest') { '' } else { "sha256:$fixtureHash" }
            size = if ($fixtureCase -eq 'size') { $fixtureBytes.Length + 1 } else { $fixtureBytes.Length }
        }) }
    }
    Write-Output -NoEnumerate @($assets)
}
function Invoke-WebRequest {
    param($Uri, $OutFile)
    if (-not $Uri.ToString().EndsWith('Winnow-0.2.0-beta.3-linux-x64.deb')) { throw 'Wrong baseline selected.' }
    $downloadState.Count++
    [IO.File]::WriteAllBytes($OutFile, $(if ($fixtureCase -eq 'bytes') { [byte[]](1,2,3) } else { $fixtureBytes }))
}
try {
    & "$PSScriptRoot/Get-PreviousDebianPackage.ps1" -Version '0.2.0-ci.9' -OutputPath $fixtureFile
    $evidence = Get-Content -LiteralPath "$fixtureFile.evidence.json" -Raw | ConvertFrom-Json
    if ($evidence.previousVersion -cne '0.2.0-beta.3' -or $evidence.runtime -cne 'linux-x64' -or
        $evidence.sha256 -cne $fixtureHash -or $evidence.size -ne $fixtureBytes.Length) { throw 'Incorrect verified baseline evidence.' }
    foreach ($fixtureCase in @('url', 'digest', 'size', 'bytes')) {
        Remove-Item -LiteralPath "$fixtureFile.evidence.json" -ErrorAction SilentlyContinue
        $before = $downloadState.Count
        $refused = $false
        try { & "$PSScriptRoot/Get-PreviousDebianPackage.ps1" -Version '0.2.0-ci.9' -OutputPath $fixtureFile }
        catch {
            if ($_.Exception.Message -notmatch '^(The previous Debian package has no valid|Previous Debian package size or checksum)') { throw }
            $refused = $true
        }
        if (-not $refused -or (Test-Path -LiteralPath "$fixtureFile.evidence.json")) { throw "Unsafe $fixtureCase baseline accepted." }
        if ($fixtureCase -in @('url', 'digest') -and $before -ne $downloadState.Count) { throw 'Untrusted metadata caused a download.' }
    }
    Write-Host 'Five Debian baseline selection and integrity contracts passed.'
} finally {
    foreach ($file in @($fixtureFile, "$fixtureFile.evidence.json")) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot }
}
