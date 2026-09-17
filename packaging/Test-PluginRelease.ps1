[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/PluginPackaging.ps1"
Assert-PluginCatalog $PackageDirectory $Version
$root = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('winnow-plugin-release-test-' + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $root | Out-Null
$catalogPath = Join-Path $root 'winnow-plugins.json'
$sourceCatalog = Join-Path $PackageDirectory 'winnow-plugins.json'
$catalog = Get-Content -LiteralPath $sourceCatalog -Raw | ConvertFrom-Json
$plugin = @(Get-FirstPartyPlugins)[0]
$record = $catalog.plugins | Where-Object id -CEQ $plugin.Id
$packagePath = Join-Path $root $record.assetName
$sourcePackage = Join-Path $PackageDirectory $record.assetName
$manifestPath = Join-Path $plugin.Directory 'plugin.json'
$sourceManifest = Get-Content -LiteralPath $manifestPath -Raw
$manifest = $sourceManifest | ConvertFrom-Json
$checks = 0

function Assert-Rejected([string]$Name, [scriptblock]$Operation, [string]$MessagePattern) {
    try { & $Operation | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $MessagePattern) { throw "Unexpected failure for ${Name}: $_" }
        $script:checks++
        return
    }
    throw "Invalid plugin release was accepted: $Name."
}

function Reset-Catalog {
    Copy-Item -LiteralPath $sourceCatalog -Destination $catalogPath -Force
}

function Edit-Catalog([scriptblock]$Change) {
    $value = Get-Content -LiteralPath $sourceCatalog -Raw | ConvertFrom-Json
    & $Change $value
    $value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $catalogPath -Encoding utf8NoBOM
}

function Set-ArchiveEntry([string]$Name, [byte[]]$Bytes) {
    $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry($Name)
        if ($entry) { $entry.Delete() }
        if ($null -ne $Bytes) {
            $stream = $archive.CreateEntry($Name).Open()
            try { $stream.Write($Bytes) }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

try {
    foreach ($asset in $catalog.plugins) {
        Copy-Item -LiteralPath (Join-Path $PackageDirectory $asset.assetName) -Destination $root
    }
    Reset-Catalog
    Assert-PluginCatalog $root $Version

    Remove-Item -LiteralPath $packagePath
    Assert-Rejected 'missing package' { Assert-PluginCatalog $root $Version } 'Missing or incorrectly named'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath
    $extraPackage = Join-Path $root 'Winnow.Plugin.Unexpected-1.0.0.zip'
    Copy-Item -LiteralPath $sourcePackage -Destination $extraPackage
    Assert-Rejected 'extra package' { Assert-PluginCatalog $root $Version } 'exactly the three'
    Remove-Item -LiteralPath $extraPackage
    Remove-Item -LiteralPath $catalogPath
    Assert-Rejected 'missing catalogue' { Assert-PluginCatalog $root $Version } 'catalogue is missing'

    foreach ($mutation in @(
        @{ Name = 'wrong release'; Change = { param($v) $v.releaseTag = 'v99.0.0' }; Pattern = 'release version' },
        @{ Name = 'wrong app version'; Change = { param($v) $v.appVersion = '99.0.0' }; Pattern = 'release version' },
        @{ Name = 'wrong schema'; Change = { param($v) $v.schemaVersion = 2 }; Pattern = 'schema' },
        @{ Name = 'missing plugin'; Change = { param($v) $v.plugins = @($v.plugins | Select-Object -First 2) }; Pattern = 'plugin count' },
        @{ Name = 'duplicate plugin'; Change = { param($v) $v.plugins[1] = $v.plugins[0] }; Pattern = 'exactly one' },
        @{ Name = 'wrong digest'; Change = { param($v) $v.plugins[0].sha256 = '0' * 64 }; Pattern = 'sha256 differs' },
        @{ Name = 'wrong size'; Change = { param($v) $v.plugins[0].size += 1 }; Pattern = 'size differs' },
        @{ Name = 'wrong version'; Change = { param($v) $v.plugins[0].version = '99.0.0' }; Pattern = 'version differs' },
        @{ Name = 'wrong asset'; Change = { param($v) $v.plugins[0].assetName = '../outside.zip' }; Pattern = 'assetName differs' }
    )) {
        Edit-Catalog $mutation.Change
        Assert-Rejected $mutation.Name { Assert-PluginCatalog $root $Version } $mutation.Pattern
    }
    Reset-Catalog

    Set-ArchiveEntry 'README.md' ([Text.Encoding]::UTF8.GetBytes('Changed package contents'))
    Assert-Rejected 'changed ZIP bytes' { Assert-PluginCatalog $root $Version } '(size|sha256) differs'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force
    Set-ArchiveEntry 'Winnow.PluginSdk.dll' ([byte[]](1, 2, 3))
    Assert-Rejected 'bundled SDK' { Read-PluginPackageRecord $packagePath $manifestPath } 'must contain only'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force
    Set-ArchiveEntry '../outside.txt' ([byte[]](1, 2, 3))
    Assert-Rejected 'archive path traversal' { Read-PluginPackageRecord $packagePath $manifestPath } 'must contain only'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force
    Set-ArchiveEntry $manifest.entryAssembly $null
    Assert-Rejected 'missing DLL' { Read-PluginPackageRecord $packagePath $manifestPath } 'must contain only'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force
    Set-ArchiveEntry 'plugin.json' ([Text.Encoding]::UTF8.GetBytes($sourceManifest + ' '))
    Assert-Rejected 'changed manifest' { Read-PluginPackageRecord $packagePath $manifestPath } 'manifest differs'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force

    $otherPackage = [IO.Compression.ZipFile]::OpenRead((Join-Path $root $catalog.plugins[1].assetName))
    $otherStream = $otherPackage.GetEntry('Winnow.Plugin.Xbox.dll').Open()
    $bytes = [IO.MemoryStream]::new()
    try { $otherStream.CopyTo($bytes); Set-ArchiveEntry $manifest.entryAssembly $bytes.ToArray() }
    finally { $bytes.Dispose(); $otherStream.Dispose(); $otherPackage.Dispose() }
    Assert-Rejected 'wrong DLL identity' { Read-PluginPackageRecord $packagePath $manifestPath } 'assembly name or version'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force

    $badSource = Join-Path $root 'source-manifest.json'
    $manifest.entryType = 'Winnow.Plugin.SteamGridDb.MissingEntryPoint'
    $text = $manifest | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($badSource, $text)
    Set-ArchiveEntry 'plugin.json' ([Text.Encoding]::UTF8.GetBytes($text))
    Assert-Rejected 'missing entry type' { Read-PluginPackageRecord $packagePath $badSource } 'entry type is absent'
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force
    $manifest = $sourceManifest | ConvertFrom-Json
    $manifest.version = '65535.0.0'
    $text = $manifest | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($badSource, $text)
    Set-ArchiveEntry 'plugin.json' ([Text.Encoding]::UTF8.GetBytes($text))
    $otherVersionPackage = Join-Path $root ($plugin.Project + '-65535.0.0.zip')
    Copy-Item -LiteralPath $packagePath -Destination $otherVersionPackage
    Assert-Rejected 'DLL version differs from manifest' { Read-PluginPackageRecord $otherVersionPackage $badSource } 'assembly name or version'
    Remove-Item -LiteralPath $otherVersionPackage
    Copy-Item -LiteralPath $sourcePackage -Destination $packagePath -Force
    Reset-Catalog
    Assert-PluginCatalog $root $Version
    Write-Output "Plugin release checks passed: valid catalogue and $checks rejected mutations."
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$root.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($root) -notlike 'winnow-plugin-release-test-*') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
