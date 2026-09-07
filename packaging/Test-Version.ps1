$ErrorActionPreference = 'Stop'
foreach ($version in @('0.1.0', '1.2.3-beta.1', '1.2.3-rc.0', '65535.0.0')) {
    $actual = & "$PSScriptRoot/Resolve-Version.ps1" -Version $version
    if ($actual.Version -cne $version -or $actual.Numeric -ne $version.Split('-')[0]) {
        throw "Incorrect version projection for $version."
    }
    if ($actual.Prerelease -ne $version.Contains('-')) { throw "Incorrect prerelease flag for $version." }
}
foreach ($version in @('v1.2.3', '1.2', '01.2.3', '1.2.3-beta.01', '1.2.3+build', '65536.0.0', '1.2.3;echo bad', "1.2.3`nother=bad")) {
    $rejected = $false
    try { & "$PSScriptRoot/Resolve-Version.ps1" -Version $version | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw "Unsafe version was accepted: $version" }
}
Write-Output 'Release version validation passed.'
