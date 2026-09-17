# Shared by release packaging and its mutation tests. Provider assemblies are inspected as metadata only.
$ErrorActionPreference = 'Stop'

function Get-FirstPartyPlugins {
    $repo = Split-Path $PSScriptRoot -Parent
    foreach ($plugin in @(
        @{ Id = 'steamgriddb'; Project = 'Winnow.Plugin.SteamGridDb' },
        @{ Id = 'xbox'; Project = 'Winnow.Plugin.Xbox' },
        @{ Id = 'psn'; Project = 'Winnow.Plugin.Psn' }
    )) {
        $directory = Join-Path $repo ('plugins/' + $plugin.Project)
        [pscustomobject]@{ Id = $plugin.Id; Project = $plugin.Project; Directory = $directory }
    }
}

function Read-PluginPackageRecord([string]$PackagePath, [string]$SourceManifest) {
    $sourceText = [IO.File]::ReadAllText($SourceManifest)
    $source = $sourceText | ConvertFrom-Json
    $version = & "$PSScriptRoot/Resolve-Version.ps1" -Version $source.version
    if ($version.Prerelease -or $source.id -cnotmatch '\A[a-z][a-z0-9.-]{0,63}\z' -or
        [string]::IsNullOrWhiteSpace($source.name) -or $source.name.Length -gt 100 -or
        [string]::IsNullOrWhiteSpace($source.description) -or $source.description.Length -gt 2000 -or
        $source.apiVersion -ne 1 -or $source.entryAssembly -cnotmatch '\AWinnow\.Plugin\.[A-Za-z0-9]+\.dll\z' -or
        [string]::IsNullOrWhiteSpace($source.entryType)) {
        throw 'The plugin source manifest has an invalid package identity.'
    }
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($source.entryAssembly)
    $assetName = "$assemblyName-$($source.version).zip"
    if ([IO.Path]::GetFileName($PackagePath) -cne $assetName -or !(Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw "Missing or incorrectly named plugin package: $assetName."
    }
    $required = @('plugin.json', $source.entryAssembly, "$assemblyName.deps.json", 'README.md')
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        if ($names.Count -ne $required.Count -or @($names | Sort-Object -Unique).Count -ne $required.Count -or
            @($required | Where-Object { $names -cnotcontains $_ }).Count -ne 0) {
            throw 'Plugin packages must contain only the manifest, entry DLL, dependency manifest and README at the archive root.'
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.Length -le 0 -or $entry.Length -gt 32MB -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw 'The plugin archive contains an empty, oversized or linked entry.'
            }
        }
        $manifestReader = [IO.StreamReader]::new($archive.GetEntry('plugin.json').Open())
        try { $packagedManifest = $manifestReader.ReadToEnd() }
        finally { $manifestReader.Dispose() }
        if ($packagedManifest -cne $sourceText) { throw 'The plugin manifest differs from its source.' }

        $assemblyStream = [IO.MemoryStream]::new()
        $entryStream = $archive.GetEntry($source.entryAssembly).Open()
        try { $entryStream.CopyTo($assemblyStream) }
        finally { $entryStream.Dispose() }
        $assemblyStream.Position = 0
        Add-Type -AssemblyName System.Reflection.Metadata
        $reader = [Reflection.PortableExecutable.PEReader]::new($assemblyStream)
        try {
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
            $identity = $metadata.GetAssemblyDefinition()
            if ($metadata.GetString($identity.Name) -cne $assemblyName -or
                $identity.Version -ne [version]::new($version.Numeric + '.0')) {
                throw 'The plugin assembly name or version differs from its manifest.'
            }
            $hasSdk = $false
            foreach ($handle in $metadata.AssemblyReferences) {
                $reference = $metadata.GetAssemblyReference($handle)
                $name = $metadata.GetString($reference.Name)
                if ($name -ceq 'Winnow.PluginSdk') { $hasSdk = $true }
                elseif ($name -cnotmatch '\A(?:System(?:\..+)?|Microsoft\.CSharp|Microsoft\.Win32\.[A-Za-z.]+|netstandard)\z') {
                    throw "The first-party plugin references an assembly outside the SDK and framework: $name."
                }
            }
            if (!$hasSdk) { throw 'The plugin does not reference the public SDK.' }
            $found = $false
            foreach ($handle in $metadata.TypeDefinitions) {
                $definition = $metadata.GetTypeDefinition($handle)
                $name = $metadata.GetString($definition.Namespace) + '.' + $metadata.GetString($definition.Name)
                if ($name -cne $source.entryType) { continue }
                $attributes = $definition.Attributes
                if (($attributes -band [Reflection.TypeAttributes]::VisibilityMask) -ne [Reflection.TypeAttributes]::Public -or
                    ($attributes -band [Reflection.TypeAttributes]::Abstract) -ne 0 -or
                    ($attributes -band [Reflection.TypeAttributes]::Interface) -ne 0) {
                    throw 'The plugin entry type must be a public concrete class.'
                }
                $hasConstructor = $false
                foreach ($methodHandle in $definition.GetMethods()) {
                    $method = $metadata.GetMethodDefinition($methodHandle)
                    if ($metadata.GetString($method.Name) -cne '.ctor' -or
                        ($method.Attributes -band [Reflection.MethodAttributes]::MemberAccessMask) -ne [Reflection.MethodAttributes]::Public) { continue }
                    $signature = $metadata.GetBlobBytes($method.Signature)
                    if ($signature.Length -ge 3 -and $signature[1] -eq 0) { $hasConstructor = $true }
                }
                $hasPluginInterface = $false
                foreach ($interfaceHandle in $definition.GetInterfaceImplementations()) {
                    $implementation = $metadata.GetInterfaceImplementation($interfaceHandle)
                    if ($implementation.Interface.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
                    $reference = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$implementation.Interface)
                    if ($reference.ResolutionScope.Kind -ne [Reflection.Metadata.HandleKind]::AssemblyReference) { continue }
                    $scope = $metadata.GetAssemblyReference([Reflection.Metadata.AssemblyReferenceHandle]$reference.ResolutionScope)
                    if ($metadata.GetString($scope.Name) -ceq 'Winnow.PluginSdk' -and
                        $metadata.GetString($reference.Namespace) -ceq 'Winnow.PluginSdk' -and
                        $metadata.GetString($reference.Name) -cin @('IPlugin', 'ILibrarySourcePlugin', 'IMetadataProviderPlugin', 'IArtworkProviderPlugin', 'IRecommendationFeedPlugin')) {
                        $hasPluginInterface = $true
                    }
                }
                if (!$hasConstructor -or !$hasPluginInterface) { throw 'The entry type must implement a public SDK provider interface and have a public parameterless constructor.' }
                $found = $true
                break
            }
            if (!$found) { throw 'The plugin entry type is absent from its assembly.' }
        }
        finally { $reader.Dispose(); $assemblyStream.Dispose() }
    }
    finally { $archive.Dispose() }
    $record = [ordered]@{
        id = $source.id
        name = $source.name
        description = $source.description
        version = $source.version
        apiVersion = $source.apiVersion
    }
    if ($source.minimumSdkVersion) {
        if ($source.minimumSdkVersion -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') { throw 'Invalid minimum SDK version.' }
        $record.minimumSdkVersion = $source.minimumSdkVersion
    }
    $record.assetName = $assetName
    $record.size = (Get-Item -LiteralPath $PackagePath).Length
    $record.sha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [pscustomobject]$record
}

function New-PluginCatalog([string]$PackageDirectory, [string]$Version) {
    $release = & "$PSScriptRoot/Resolve-Version.ps1" -Version $Version
    $records = @(foreach ($plugin in Get-FirstPartyPlugins) {
        $manifestPath = Join-Path $plugin.Directory 'plugin.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.id -cne $plugin.Id -or $manifest.entryAssembly -cne ($plugin.Project + '.dll')) {
            throw "The first-party plugin identity changed: $($plugin.Id)."
        }
        $package = Join-Path $PackageDirectory ($plugin.Project + '-' + $manifest.version + '.zip')
        Read-PluginPackageRecord $package $manifestPath
    })
    $archives = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter 'Winnow.Plugin.*.zip')
    if ($archives.Count -ne $records.Count) { throw 'The release must contain exactly the three first-party plugin ZIPs.' }
    [pscustomobject][ordered]@{ schemaVersion = 1; releaseTag = 'v' + $release.Version; appVersion = $release.Version; plugins = $records }
}

function Assert-PluginCatalog([string]$PackageDirectory, [string]$Version) {
    $expected = New-PluginCatalog $PackageDirectory $Version
    $path = Join-Path $PackageDirectory 'winnow-plugins.json'
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -gt 64KB) {
        throw 'The release plugin catalogue is missing or oversized.'
    }
    $actual = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($actual.schemaVersion -ne 1 -or $actual.releaseTag -cne $expected.releaseTag -or
        $actual.appVersion -cne $expected.appVersion -or @($actual.plugins).Count -ne 3) {
        throw 'The plugin catalogue schema, release version or plugin count is incorrect.'
    }
    foreach ($plugin in $expected.plugins) {
        $matches = @($actual.plugins | Where-Object { $_.id -ceq $plugin.id })
        if ($matches.Count -ne 1) { throw "The plugin catalogue requires exactly one $($plugin.id) entry." }
        foreach ($property in $plugin.PSObject.Properties) {
            if ([string]$matches[0].($property.Name) -cne [string]$property.Value) {
                throw "The plugin catalogue $($plugin.id) $($property.Name) differs from its package."
            }
        }
        if (!$plugin.minimumSdkVersion -and $matches[0].minimumSdkVersion) { throw 'Unexpected minimum SDK version.' }
    }
}
