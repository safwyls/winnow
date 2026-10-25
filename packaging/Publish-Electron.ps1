[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$ExtraProperties = @()
)

$ErrorActionPreference = 'Stop'
$release = & "$PSScriptRoot/Resolve-Version.ps1" -Version $Version
$repo = Split-Path $PSScriptRoot -Parent
$frontend = Join-Path $repo 'src/Winnow.Electron'
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $repo 'Version.props')
if ($release.Numeric -ne $versionProps.Project.PropertyGroup.VersionPrefix) {
    throw 'Package version must use the base declared in Version.props.'
}
if (($Runtime -eq 'win-x64' -and !$IsWindows) -or ($Runtime -eq 'linux-x64' -and !$IsLinux)) {
    throw 'Publish on the target operating system so native Electron resources retain their platform metadata.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw 'Publish output must be empty so stale files cannot enter a release.'
}
$scratch = Join-Path $repo ('.tmp/electron-publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output, $scratch | Out-Null
$buildOutput = Join-Path $scratch 'dotnet'
$readyToRun = @()
if ($Runtime -eq 'win-x64') { $readyToRun = @('-p:PublishReadyToRun=true') }
$properties = @(
    "-p:Version=$Version", "-p:AssemblyVersion=$($release.Numeric).0", "-p:FileVersion=$($release.Numeric).0",
    "-p:SourceRevisionId=$Commit", '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
    '-p:ContinuousIntegrationBuild=true'
)
$previousVersion = $env:WINNOW_BUILD_VERSION
$previousCommit = $env:WINNOW_BUILD_COMMIT
try {
    $env:WINNOW_BUILD_VERSION = $Version
    $env:WINNOW_BUILD_COMMIT = $Commit.ToLowerInvariant()
    Push-Location $frontend
    try {
        & node node_modules/typescript/bin/tsc --noEmit
        if ($LASTEXITCODE -ne 0) { throw 'Frontend typecheck failed.' }
        & node node_modules/electron-vite/bin/electron-vite.js build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        & node scripts/third-party-notices.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Frontend notice generation failed.' }
        # This primary directory feeds the existing Inno/ZIP and deb/tar packagers.
        # It deliberately does not inherit the secondary NSIS/AppImage backend layout.
        $configuration = @{
            appId = 'app.winnow.afterglow'; productName = 'Winnow'; asar = $true
            buildVersion = "$($release.Numeric).0"
            directories = @{ output = (Join-Path $scratch 'electron') }
            files = @('out/**/*', 'package.json')
            extraMetadata = @{ version = $Version; shortVersionWindows = "$($release.Numeric).0" }
            extraResources = @(
                @{ from = 'resources/icon.ico'; to = 'icon.ico' },
                @{ from = '.staging/THIRD-PARTY-NOTICES.md'; to = 'THIRD-PARTY-NOTICES.md' }
            )
            win = @{ icon = 'resources/icon.ico'; executableName = 'Winnow'; signExecutable = $false }
            linux = @{ executableName = 'Winnow'; category = 'Game' }
            npmRebuild = $false
            publish = $null
        }
        $configurationPath = Join-Path $scratch 'electron-builder.json'
        $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
        $platform = if ($Runtime -eq 'win-x64') { '--win' } else { '--linux' }
        & node node_modules/electron-builder/out/cli/cli.js --config $configurationPath $platform --x64 --dir --publish never
        if ($LASTEXITCODE -ne 0) { throw 'Electron directory packaging failed.' }
    }
    finally { Pop-Location }
    $unpacked = Join-Path $scratch ('electron/' + $(if ($Runtime -eq 'win-x64') { 'win-unpacked' } else { 'linux-unpacked' }))
    Get-ChildItem -LiteralPath $unpacked -Force | Copy-Item -Destination $output -Recurse -Force
    if ($Runtime -eq 'linux-x64') {
        # Ubuntu uses the exact-path AppArmor user-namespace permission. A portable
        # directory must never distribute a privileged setuid sandbox helper.
        [IO.File]::SetUnixFileMode((Join-Path $output 'chrome-sandbox'), [IO.UnixFileMode]493)
    }
    foreach ($project in @('Winnow.Backend', 'Winnow.Update.Helper')) {
        $destination = Join-Path $output $(if ($project -eq 'Winnow.Backend') { 'backend' } else { 'update-helper' })
        & dotnet publish "$repo/src/$project/$project.csproj" --configuration Release --runtime $Runtime `
            --self-contained true --output $destination --artifacts-path $buildOutput `
            @properties @readyToRun @ExtraProperties -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "$project publish failed ($LASTEXITCODE)." }
    }
    # Plugins retain their independent manifest version and use the SDK supplied by the backend.
    $pluginSource = Join-Path $repo 'plugins/Winnow.Plugin.SteamGridDb'
    $pluginManifest = Get-Content -LiteralPath (Join-Path $pluginSource 'plugin.json') -Raw | ConvertFrom-Json
    $pluginVersion = & "$PSScriptRoot/Resolve-Version.ps1" -Version $pluginManifest.version
    $pluginStage = Join-Path $scratch 'plugin'
    & dotnet publish "$pluginSource/Winnow.Plugin.SteamGridDb.csproj" --configuration Release --self-contained false `
        --output $pluginStage --artifacts-path (Join-Path $scratch 'plugin-build') `
        "-p:Version=$($pluginVersion.Version)" "-p:AssemblyVersion=$($pluginVersion.Numeric).0" `
        "-p:FileVersion=$($pluginVersion.Numeric).0" '-p:ContinuousIntegrationBuild=true' -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Bundled plugin publish failed.' }
    $pluginOutput = Join-Path $output 'plugins/steamgriddb'
    New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null
    foreach ($file in @('Winnow.Plugin.SteamGridDb.dll', 'Winnow.Plugin.SteamGridDb.deps.json', 'plugin.json')) {
        Copy-Item -LiteralPath (Join-Path $pluginStage $file) -Destination $pluginOutput
    }
    Copy-Item -LiteralPath (Join-Path $pluginSource 'README.md') -Destination $pluginOutput
    @{ frontend = 'electron'; version = $Version; runtime = $Runtime; commit = $Commit.ToLowerInvariant() } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding utf8NoBOM
    & "$PSScriptRoot/Write-DotNetNotices.ps1" -PublishDirectory $output -OutputPath (Join-Path $output 'resources/DOTNET-NOTICES.md')
    & "$PSScriptRoot/Verify-ElectronPackage.ps1" -PublishDirectory $output -Runtime $Runtime -Version $Version -Commit $Commit
    Write-Output "Published Electron distribution: $output"
}
finally {
    $env:WINNOW_BUILD_VERSION = $previousVersion
    $env:WINNOW_BUILD_COMMIT = $previousCommit
    # Failed builds retain their isolated intermediates for diagnosis; outputs are never reused.
    Write-Verbose "Build intermediates: $scratch"
}
