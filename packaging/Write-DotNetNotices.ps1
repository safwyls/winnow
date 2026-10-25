[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$PackageCache
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (!$PackageCache) {
    $location = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0 -or $location -notmatch '^global-packages:\s+(.+)$') { throw 'Cannot locate the restored NuGet package cache.' }
    $PackageCache = $Matches[1].Trim()
}
$cache = (Resolve-Path -LiteralPath $PackageCache).Path
$packages = [Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($depsFile in Get-ChildItem -LiteralPath $publish -Filter '*.deps.json' -Recurse -File) {
    $deps = Get-Content -LiteralPath $depsFile.FullName -Raw | ConvertFrom-Json -AsHashtable
    foreach ($entry in $deps.libraries.GetEnumerator()) {
        if ($entry.Value.type -eq 'package') { [void]$packages.Add($entry.Key) }
    }
    # Self-contained framework notices are not consistently included in application deps libraries.
    $configPath = $depsFile.FullName.Replace('.deps.json', '.runtimeconfig.json')
    if (Test-Path -LiteralPath $configPath -PathType Leaf) {
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -AsHashtable
        $target = [string]$deps.runtimeTarget.name
        $rid = $target.Split('/')[-1]
        if ($rid -match '^(win|linux)-(x64|arm64)$' -and $config.runtimeOptions.ContainsKey('includedFrameworks')) {
            foreach ($framework in $config.runtimeOptions.includedFrameworks) {
                [void]$packages.Add("$($framework.name).Runtime.$rid/$($framework.version)")
            }
        }
    }
}
if (!$packages.Count) { throw 'No shipped NuGet dependencies were found.' }
$text = [Text.StringBuilder]::new()
[void]$text.AppendLine('# .NET dependency notices')
[void]$text.AppendLine()
[void]$text.AppendLine('Inventory of packages in the shipped backend, helper and provider dependency manifests. License declarations, authors and copyright are copied from the restored package metadata; license and notice files distributed by each package are reproduced below. Electron, Chromium and JavaScript notices accompany this file separately.')
$ledger = [Collections.Generic.List[object]]::new()
foreach ($identity in $packages) {
    if ($identity -notmatch '^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.+-]+)$') { throw "Invalid published package identity: $identity" }
    $id = $Matches[1]; $version = $Matches[2]
    $directory = Join-Path $cache $identity.ToLowerInvariant()
    $spec = Join-Path $directory ($id.ToLowerInvariant() + '.nuspec')
    if (!(Test-Path -LiteralPath $spec -PathType Leaf)) { throw "Missing restored package metadata: $identity" }
    [xml]$xml = Get-Content -LiteralPath $spec -Raw
    $metadata = $xml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    function MetadataText([string]$Name) {
        $node = $metadata.SelectSingleNode("*[local-name()='$Name']")
        if ($node) { return $node.InnerText.Trim() }
        return ''
    }
    $licenseNode = $metadata.SelectSingleNode('*[local-name()="license"]')
    $license = if ($licenseNode) { $licenseNode.InnerText.Trim() } else { MetadataText 'licenseUrl' }
    if (!$license) { throw "Package has no license declaration: $identity" }
    $authors = MetadataText 'authors'
    $copyright = MetadataText 'copyright'
    [void]$text.AppendLine("`n## $id $version`n")
    [void]$text.AppendLine("Package: https://www.nuget.org/packages/$id/$version")
    [void]$text.AppendLine("`nDeclared license: $license")
    if ($licenseNode -and $licenseNode.GetAttribute('type') -eq 'expression') {
        [void]$text.AppendLine("License reference: https://licenses.nuget.org/$license")
    }
    if ($authors) { [void]$text.AppendLine("Authors: $authors") }
    if ($copyright) { [void]$text.AppendLine("Copyright: $copyright") }
    $files = [Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match '^(licen[sc]e|copying|notice|third.party.notices)(\.|$)') {
        [void]$files.Add($file.FullName)
    }
    if ($licenseNode -and $licenseNode.GetAttribute('type') -eq 'file') {
        $declaredPath = [IO.Path]::GetFullPath((Join-Path $directory $license))
        if (!$declaredPath.StartsWith([IO.Path]::GetFullPath($directory) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "License file escapes package: $identity"
        }
        if (!(Test-Path -LiteralPath $declaredPath -PathType Leaf)) { throw "Missing declared license file: $identity" }
        [void]$files.Add($declaredPath)
    }
    $records = @()
    foreach ($file in $files) {
        if ((Get-Item -LiteralPath $file).Length -gt 8MB) { throw "Unexpectedly large notice file: $identity" }
        $relative = [IO.Path]::GetRelativePath($directory, $file)
        [void]$text.AppendLine("`n### $relative`n")
        [void]$text.AppendLine((Get-Content -LiteralPath $file -Raw))
        $records += @{ file = $relative; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $ledger.Add(@{ id = $id; version = $version; license = $license; notices = $records })
}
$output = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
[IO.File]::WriteAllText($output, $text.ToString(), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText([IO.Path]::ChangeExtension($output, '.json'), (ConvertTo-Json -InputObject @($ledger) -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Collected .NET metadata and available source notices for $($packages.Count) shipped package versions."
