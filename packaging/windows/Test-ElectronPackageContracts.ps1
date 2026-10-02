[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-ElectronPackageLayout.ps1')

# Pure package-byte fixtures. No installer, registry, frontend or backend is launched.
$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-package-contract-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$checks = 0
function Assert-Rejected([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        return
    }
    throw "Expected rejection containing '$Expected'."
}
try {
    $legacyEntries = Get-WinnowWindowsRequiredFiles $false
    Assert-WinnowWindowsEntries ($legacyEntries + 'Winnow.dll' + 'Avalonia.Base.dll') $false
    $checks++
    $electronEntries = Get-WinnowWindowsRequiredFiles $true
    Assert-WinnowWindowsEntries $electronEntries $true
    $checks++
    foreach ($required in $electronEntries) {
        Assert-Rejected { Assert-WinnowWindowsEntries @($electronEntries | Where-Object { $_ -cne $required }) $true } $required
        $checks++
    }
    foreach ($unexpected in @('appsettings.local.json', 'backend/appsettings.local.json', 'Winnow.dll',
        'Avalonia.Base.dll', 'backend/Avalonia.Controls.dll', 'Winnow.Auth.WebView.dll',
        'Winnow.Covers.Avalonia.dll', 'winnow.db', 'backend/private.db-wal', 'account.secrets.json', 'backend/endpoint.json')) {
        Assert-Rejected { Assert-WinnowWindowsEntries ($electronEntries + $unexpected) $true } $unexpected
        $checks++
    }
    Assert-Rejected { Assert-WinnowWindowsEntries ($legacyEntries + 'backend/appsettings.local.json') $false } 'local configuration'
    $checks++
    foreach ($relative in $electronEntries) {
        $file = Join-Path $root $relative
        $null = New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force
        [IO.File]::WriteAllText($file, 'package fixture')
    }
    [IO.File]::WriteAllText((Join-Path $root 'release-info.json'), '{"frontend":"electron"}')
    Assert-WinnowWindowsDirectory $root
    $hashLines = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object Name -ne 'PACKAGE-SHA256SUMS' | ForEach-Object {
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/'))"
    })
    $hashPath = Join-Path $root 'PACKAGE-SHA256SUMS'
    $hashLines | Set-Content -LiteralPath $hashPath -Encoding utf8NoBOM
    Assert-WinnowPackagedHashes $root
    [IO.File]::WriteAllText((Join-Path $root 'resources/app.asar'), 'tampered package')
    Assert-Rejected { Assert-WinnowPackagedHashes $root } 'published bytes'
    [IO.File]::WriteAllText((Join-Path $root 'resources/app.asar'), 'package fixture')
    @($hashLines + $hashLines[0]) | Set-Content -LiteralPath $hashPath -Encoding utf8NoBOM
    Assert-Rejected { Assert-WinnowPackagedHashes $root } 'duplicate path'
    @($hashLines | Where-Object { -not $_.EndsWith('  resources/app.asar') }) | Set-Content -LiteralPath $hashPath -Encoding utf8NoBOM
    Assert-Rejected { Assert-WinnowPackagedHashes $root } 'omits resources/app.asar'
    @($hashLines + (('0' * 64) + '  ../outside')) | Set-Content -LiteralPath $hashPath -Encoding utf8NoBOM
    Assert-Rejected { Assert-WinnowPackagedHashes $root } 'unsafe or duplicate path'
    $hashLines | Set-Content -LiteralPath $hashPath -Encoding utf8NoBOM
    $checks += 5
    $electron = Get-WinnowFrontendPayload $root
    if (-not $electron.Electron -or $electron.RelativePath -cne 'resources/app.asar') { throw 'Electron fingerprint did not use ASAR.' }
    $checks++
    $data = Join-Path $root 'user-data'
    $null = New-Item -ItemType Directory -Path $data
    [IO.File]::WriteAllText((Join-Path $data 'winnow.db'), 'preserve data')
    Assert-Rejected { Assert-WinnowWindowsDirectory $root } 'user state'
    Assert-WinnowWindowsDirectory $root $data
    Assert-Rejected { Assert-WinnowWindowsDirectory $root $root } 'entire package'
    $checks += 3
    [IO.File]::WriteAllText((Join-Path $root 'release-info.json'), '{"version":"legacy"}')
    [IO.File]::WriteAllText((Join-Path $root 'Winnow.dll'), 'old frontend')
    $legacy = Get-WinnowFrontendPayload $root
    if ($legacy.Electron -or $legacy.RelativePath -cne 'Winnow.dll' -or (Test-WinnowSameFrontendPayload $electron $legacy)) {
        throw 'Legacy DLL and Electron ASAR identities were conflated.'
    }
    if (-not (Test-WinnowSameFrontendPayload $legacy (Get-WinnowFrontendPayload $root))) { throw 'Unchanged legacy payload was not stable.' }
    $checks += 2
    Write-Host "Passed $checks Windows package contract checks. No installation or OS registration was performed."
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^Winnow-package-contract-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the exact package fixture root.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
