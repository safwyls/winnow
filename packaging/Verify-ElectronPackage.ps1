[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit
)

$ErrorActionPreference = 'Stop'
$release = & "$PSScriptRoot/Resolve-Version.ps1" -Version $Version
$output = (Resolve-Path -LiteralPath $PublishDirectory).Path
$repo = Split-Path $PSScriptRoot -Parent
$suffix = if ($Runtime -eq 'win-x64') { '.exe' } else { '' }
$requiredFiles = @(
    "Winnow$suffix", 'resources/app.asar', 'resources/icon.ico', 'resources/THIRD-PARTY-NOTICES.md',
    'resources/DOTNET-NOTICES.md', 'LICENSE.electron.txt', 'LICENSES.chromium.html', 'icudtl.dat', 'resources.pak',
    'v8_context_snapshot.bin', 'release-info.json', "backend/Winnow.Backend$suffix",
    'backend/Winnow.Backend.dll', 'backend/Winnow.Backend.deps.json', 'backend/Winnow.Backend.runtimeconfig.json',
    'backend/Microsoft.AspNetCore.dll', "update-helper/Winnow.Update.Helper$suffix",
    'update-helper/Winnow.Update.Helper.dll', 'update-helper/Winnow.Update.Helper.deps.json',
    'update-helper/Winnow.Update.Helper.runtimeconfig.json', 'plugins/steamgriddb/README.md',
    'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.deps.json'
)
foreach ($directory in @('backend', 'update-helper')) {
    foreach ($library in $(if ($Runtime -eq 'win-x64') { @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll') } else { @('libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so') })) {
        $requiredFiles += "$directory/$library"
    }
}
foreach ($file in $requiredFiles) {
    $path = Join-Path $output $file
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Missing or empty package file: $file"
    }
}
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -ne 'PACKAGE-SHA256SUMS')
if ($files | Where-Object {
    $_.Name -like 'Avalonia*.dll' -or $_.Name -in @('Winnow.dll', 'Winnow.Auth.WebView.dll', 'Winnow.Covers.Avalonia.dll', 'appsettings.local.json') -or
    $_.Name -like '*.secrets.json' -or $_.Name -match '\.db(?:-(?:shm|wal))?$'
}) { throw 'The Electron package contains an Avalonia UI assembly, local configuration, secret or database.' }
$manifest = Get-Content -LiteralPath (Join-Path $output 'release-info.json') -Raw | ConvertFrom-Json
if ($manifest.frontend -cne 'electron' -or $manifest.version -cne $Version -or
    $manifest.runtime -cne $Runtime -or $manifest.commit -cne $Commit.ToLowerInvariant()) {
    throw 'The release manifest does not match the requested frontend, version, runtime and source.'
}
foreach ($component in @(@('backend', 'Winnow.Backend'), @('update-helper', 'Winnow.Update.Helper'))) {
    $base = Join-Path $output ($component[0] + '/' + $component[1])
    $config = Get-Content -LiteralPath "$base.runtimeconfig.json" -Raw | ConvertFrom-Json
    if (!($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App')) {
        throw "$($component[1]) must include its .NET runtime."
    }
    if ($component[0] -eq 'backend' -and !($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.AspNetCore.App')) {
        throw 'The backend must include its independent ASP.NET runtime.'
    }
    $identity = [Diagnostics.FileVersionInfo]::GetVersionInfo("$base.dll")
    if ($identity.ProductVersion -cne "$Version+$($Commit.ToLowerInvariant())" -or $identity.FileVersion -ne "$($release.Numeric).0") {
        throw "Incorrect managed component identity: $($component[1])."
    }
}
if ($Runtime -eq 'win-x64') {
    $identity = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'Winnow.exe'))
    if ($identity.ProductName -cne 'Winnow' -or $identity.ProductVersion -cne "$($release.Numeric).0" -or
        $identity.FileVersion -cne "$($release.Numeric).0") {
        throw "Incorrect Electron executable identity: $($identity.ProductName), $($identity.ProductVersion), $($identity.FileVersion)."
    }
}
$icon = Join-Path $output 'resources/icon.ico'
if ((Get-FileHash -LiteralPath $icon).Hash -cne (Get-FileHash -LiteralPath (Join-Path $repo 'src/Winnow.Electron/resources/icon.ico')).Hash) {
    throw 'The packaged product icon differs from its source.'
}
& "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $output
& node (Join-Path $repo 'src/Winnow.Electron/scripts/verify-primary-asar.mjs') (Join-Path $output 'resources/app.asar') $Version $Commit.ToLowerInvariant()
if ($LASTEXITCODE -ne 0) { throw 'Electron ASAR verification failed.' }
$hashes = $files | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
}
# A complete manifest permits later archive/install checks without executing package code.
$hashes | Set-Content -LiteralPath (Join-Path $output 'PACKAGE-SHA256SUMS') -Encoding utf8NoBOM
Write-Output "Verified Electron $Version ($Runtime), $($files.Count) files."
