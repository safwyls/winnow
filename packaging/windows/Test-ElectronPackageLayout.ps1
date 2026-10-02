# Read-only package contracts shared by archive construction and disposable upgrade smokes.
function Test-WinnowElectronManifest($Manifest) {
    return $null -ne $Manifest.PSObject.Properties['frontend'] -and $Manifest.frontend -ceq 'electron'
}

function Get-WinnowWindowsRequiredFiles([bool]$Electron) {
    $files = @('Winnow.exe', 'release-info.json', 'backend/Winnow.Backend.exe', 'backend/Winnow.Backend.dll',
        'backend/Winnow.Backend.runtimeconfig.json', 'backend/Microsoft.AspNetCore.dll')
    if ($Electron) {
        $files += @('resources/app.asar', 'resources/icon.ico', 'resources/THIRD-PARTY-NOTICES.md', 'resources/DOTNET-NOTICES.md', 'PACKAGE-SHA256SUMS',
            'LICENSE.electron.txt', 'LICENSES.chromium.html', 'chrome_100_percent.pak', 'chrome_200_percent.pak',
            'resources.pak', 'icudtl.dat', 'v8_context_snapshot.bin', 'locales/en-US.pak',
            'ffmpeg.dll', 'libEGL.dll', 'libGLESv2.dll', 'backend/Winnow.Backend.deps.json',
            'update-helper/Winnow.Update.Helper.exe', 'update-helper/Winnow.Update.Helper.dll',
            'update-helper/Winnow.Update.Helper.runtimeconfig.json', 'update-helper/Winnow.Update.Helper.deps.json',
            'plugins/steamgriddb/plugin.json', 'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.dll')
    }
    return $files
}

function Assert-WinnowWindowsEntries([string[]]$Entries, [bool]$Electron) {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Entries) { $null = $names.Add($entry.Replace('\', '/')) }
    foreach ($required in (Get-WinnowWindowsRequiredFiles $Electron)) {
        if (-not $names.Contains($required)) { throw "Windows package is missing $required." }
    }
    foreach ($entry in $names) {
        if ($entry -match '(^|/)appsettings\.local\.json$') { throw "Windows package contains local configuration: $entry" }
        if ($Electron -and $entry -match '(^|/)(Avalonia[^/]*\.dll|Winnow\.dll|Winnow\.Auth\.WebView\.dll|Winnow\.Covers\.Avalonia\.dll)$') {
            throw "Electron package contains an Avalonia frontend assembly: $entry"
        }
        if ($Electron -and $entry -match '(^|/)([^/]*\.db(?:-wal|-shm)?|[^/]*\.secrets\.json|endpoint\.json)$') {
            throw "Electron package contains runtime user state: $entry"
        }
    }
}

function Assert-WinnowWindowsDirectory([string]$Directory, [string]$SelectedDataDirectory) {
    $directoryRoot = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    $selectedPrefix = if ($SelectedDataDirectory) { [IO.Path]::GetFullPath($SelectedDataDirectory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar } else { $null }
    if ($selectedPrefix -and $selectedPrefix.TrimEnd('\', '/') -ieq $directoryRoot) {
        throw 'The selected data directory cannot exclude the entire package from inspection.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'release-info.json') -Raw | ConvertFrom-Json
    $entries = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object {
        -not $selectedPrefix -or -not $_.FullName.StartsWith($selectedPrefix, [StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        [IO.Path]::GetRelativePath($Directory, $_.FullName)
    })
    Assert-WinnowWindowsEntries $entries (Test-WinnowElectronManifest $manifest)
}

function Assert-WinnowPackagedHashes([string]$Directory) {
    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'release-info.json') -Raw | ConvertFrom-Json
    if (-not (Test-WinnowElectronManifest $manifest)) { return }
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in (Get-Content -LiteralPath (Join-Path $Directory 'PACKAGE-SHA256SUMS'))) {
        if ($line -cnotmatch '^([0-9a-f]{64})  (.+)$') { throw 'Invalid package hash manifest line.' }
        $digest = $Matches[1]
        $relative = $Matches[2]
        $file = [IO.Path]::GetFullPath((Join-Path $Directory $relative))
        if ([IO.Path]::IsPathFullyQualified($relative) -or -not $file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
            -not $seen.Add($relative.Replace('\', '/'))) { throw 'Package hash manifest contains an unsafe or duplicate path.' }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $digest) {
            throw "Package file does not match its published bytes: $relative"
        }
    }
    foreach ($required in (Get-WinnowWindowsRequiredFiles $true | Where-Object { $_ -cne 'PACKAGE-SHA256SUMS' })) {
        if (-not $seen.Contains($required)) { throw "Package hash manifest omits $required." }
    }
}

function Get-WinnowFrontendPayload([string]$Directory) {
    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'release-info.json') -Raw | ConvertFrom-Json
    $electron = Test-WinnowElectronManifest $manifest
    $relative = if ($electron) { 'resources/app.asar' } else { 'Winnow.dll' }
    $path = Join-Path $Directory $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Frontend payload is missing: $path" }
    return [pscustomobject]@{ RelativePath = $relative; Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; Electron = $electron }
}

function Test-WinnowSameFrontendPayload($Left, $Right) {
    return $Left.RelativePath -ceq $Right.RelativePath -and $Left.Hash -ceq $Right.Hash
}

function Invoke-WinnowPackagedProbe([string]$Executable, [string]$DataDirectory, [string]$Report, [string]$Mode) {
    $manifest = Get-Content -LiteralPath (Join-Path (Split-Path $Executable -Parent) 'release-info.json') -Raw | ConvertFrom-Json
    if (-not (Test-WinnowElectronManifest $manifest)) { return }
    $probe = Join-Path $PSScriptRoot '../../src/Winnow.Electron/tests/packaged/probe.mjs'
    & node $probe --exe $Executable --data-dir $DataDirectory --report $Report --mode $Mode
    if ($LASTEXITCODE -ne 0) { throw "Packaged Electron $Mode identity/window/activation probe failed ($LASTEXITCODE)." }
}
