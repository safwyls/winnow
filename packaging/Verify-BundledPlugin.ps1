[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$SourceManifest = (Join-Path (Split-Path $PSScriptRoot -Parent) 'plugins/Winnow.Plugin.SteamGridDb/plugin.json')
)

$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $SourceManifest -Raw | ConvertFrom-Json
if ($source.id -notmatch '^[a-z][a-z0-9.-]*$' -or
    $source.entryAssembly -notmatch '^[A-Za-z0-9_.-]+\.dll$' -or
    [string]::IsNullOrWhiteSpace($source.entryType)) {
    throw 'The bundled plugin manifest has an invalid package identity.'
}
$pluginDirectory = Join-Path $PublishDirectory ('plugins/' + $source.id)
$manifest = Join-Path $pluginDirectory 'plugin.json'
$assembly = Join-Path $pluginDirectory $source.entryAssembly
if (!(Test-Path -LiteralPath $manifest -PathType Leaf) -or !(Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "The bundled plugin package is incomplete: $($source.id)."
}
if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne
    (Get-FileHash -LiteralPath $SourceManifest -Algorithm SHA256).Hash) {
    throw "The bundled plugin manifest differs from its source: $($source.id)."
}
$identity = [Reflection.AssemblyName]::GetAssemblyName($assembly)
if ($identity.Name -cne [IO.Path]::GetFileNameWithoutExtension($source.entryAssembly)) {
    throw 'The bundled plugin assembly does not match its manifest.'
}

# Inspect metadata without loading or executing provider code during packaging.
Add-Type -AssemblyName System.Reflection.Metadata
$stream = [IO.File]::OpenRead($assembly)
$reader = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
    $found = $false
    foreach ($handle in $metadata.TypeDefinitions) {
        $definition = $metadata.GetTypeDefinition($handle)
        $name = $metadata.GetString($definition.Name)
        $namespace = $metadata.GetString($definition.Namespace)
        if (($namespace + '.' + $name) -ceq $source.entryType) { $found = $true; break }
    }
    if (!$found) { throw 'The bundled plugin entry type is absent from its assembly.' }
}
finally { $reader.Dispose(); $stream.Dispose() }

Write-Output "Verified bundled plugin $($source.id) ($($source.entryAssembly))."
