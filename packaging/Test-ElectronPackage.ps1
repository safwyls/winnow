[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $source 'release-info.json') -Raw | ConvertFrom-Json
$repo = Split-Path $PSScriptRoot -Parent
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $repo '.tmp')) + [IO.Path]::DirectorySeparatorChar
$scratch = Join-Path $scratchRoot ('electron-package-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
function Verify {
    & "$PSScriptRoot/Verify-ElectronPackage.ps1" -PublishDirectory $scratch `
        -Version $manifest.version -Runtime $manifest.runtime -Commit $manifest.commit | Out-Null
}
function Refuses([scriptblock]$mutation, [scriptblock]$restore, [string]$message) {
    try {
        & $mutation
        $refused = $false
        try { Verify } catch {
            if ($_.Exception.Message -notmatch $message) { throw }
            $refused = $true
        }
        if (!$refused) { throw 'Invalid package was accepted.' }
        $script:cases++
    }
    finally { & $restore }
}
try {
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $scratch -Recurse -Force
    Verify
    $script:cases = 1
    foreach ($relative in @('resources/app.asar', 'resources/DOTNET-NOTICES.md', 'backend/Winnow.Backend.dll', 'update-helper/Winnow.Update.Helper.dll')) {
        $path = Join-Path $scratch $relative
        Refuses { Remove-Item -LiteralPath $path } { Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $path } 'Missing or empty package file'
    }
    foreach ($relative in @('backend/Avalonia.Controls.dll', 'appsettings.local.json', 'backend/fixture.secrets.json', 'backend/fixture.db-wal')) {
        $path = Join-Path $scratch $relative
        Refuses { Set-Content -LiteralPath $path -Value 'not permitted' } { Remove-Item -LiteralPath $path } 'Avalonia UI assembly, local configuration, secret or database'
    }
    foreach ($property in @('frontend', 'version', 'runtime', 'commit')) {
        $path = Join-Path $scratch 'release-info.json'
        Refuses {
            $changed = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            $changed.$property = 'wrong'
            $changed | ConvertTo-Json | Set-Content -LiteralPath $path
        } { Copy-Item -LiteralPath (Join-Path $source 'release-info.json') -Destination $path -Force } 'release manifest'
    }
    $relative = 'backend/Winnow.Backend.runtimeconfig.json'
    $path = Join-Path $scratch $relative
    Refuses {
        '{"runtimeOptions":{"frameworks":[{"name":"Microsoft.AspNetCore.App","version":"10.0.0"}]}}' | Set-Content -LiteralPath $path
    } { Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $path -Force } 'include its .NET runtime'
    $relative = 'resources/icon.ico'
    $path = Join-Path $scratch $relative
    Refuses { Set-Content -LiteralPath $path -Value 'wrong icon' } {
        Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $path -Force
    } 'product icon differs'
    Verify
    $script:cases++
    Write-Output "Passed $script:cases Electron package contracts, including intact package before and after mutations."
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (!$resolved.StartsWith($scratchRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'electron-package-contract-*') { throw 'Unsafe package test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
