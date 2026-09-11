[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory)

$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('winnow-plugin-package-' + [Guid]::NewGuid().ToString('N'))
$root = [IO.Path]::GetFullPath($root)
$pluginDirectory = Join-Path $root 'plugins/steamgriddb'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'plugins/Winnow.Plugin.SteamGridDb/plugin.json'
$builtAssembly = Join-Path $BuildDirectory 'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.dll'
$manifest = Join-Path $pluginDirectory 'plugin.json'
$assembly = Join-Path $pluginDirectory 'Winnow.Plugin.SteamGridDb.dll'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null

function Assert-Rejected([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw 'An invalid bundled package was accepted.' }
}

try {
    Copy-Item -LiteralPath $source -Destination $manifest
    Copy-Item -LiteralPath $builtAssembly -Destination $assembly
    & "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $root

    Remove-Item -LiteralPath $assembly
    Assert-Rejected { & "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $root }
    Copy-Item -LiteralPath (Join-Path $BuildDirectory 'Winnow.Core.dll') -Destination $assembly
    Assert-Rejected { & "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $root }
    Copy-Item -LiteralPath $builtAssembly -Destination $assembly -Force

    Add-Content -LiteralPath $manifest -Value ' '
    Assert-Rejected { & "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $root }

    $bad = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
    $bad.entryType = 'Winnow.Plugin.SteamGridDb.MissingEntryPoint'
    $badSource = Join-Path $root 'source-manifest.json'
    $bad | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $badSource -Encoding utf8NoBOM
    Copy-Item -LiteralPath $badSource -Destination $manifest -Force
    Assert-Rejected { & "$PSScriptRoot/Verify-BundledPlugin.ps1" -PublishDirectory $root -SourceManifest $badSource }

    Write-Output 'Bundled plugin checks passed: valid, missing assembly, mismatched assembly, changed manifest, missing entry type.'
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$root.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($root) -notlike 'winnow-plugin-package-*') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
