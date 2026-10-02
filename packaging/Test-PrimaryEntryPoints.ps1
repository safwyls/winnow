[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-entrypoints-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $root 'packaging'), (Join-Path $root 'src/Winnow.Electron') -Force
$script:checks = 0
$initialExitVariable = Get-Variable LASTEXITCODE -ErrorAction SilentlyContinue
$hadExitCode = $null -ne $initialExitVariable
$initialExitCode = if ($hadExitCode) { $initialExitVariable.Value } else { $null }
$global:WinnowEntryPointCommands = [Collections.Generic.List[object]]::new()
$global:WinnowEntryPointNodeExit = 0
$global:WinnowEntryPointDotnetExit = 0
$global:WinnowEntryPointNpmExit = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Assert-Failure([scriptblock]$Action, [string]$Message) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    Assert-True ($failure -eq $Message) "Expected failure: $Message; received: $failure"
}
function dotnet {
    $global:WinnowEntryPointCommands.Add([pscustomobject]@{ file = 'dotnet'; arguments = @($args) })
    $global:LASTEXITCODE = $global:WinnowEntryPointDotnetExit
}
function npm {
    $global:WinnowEntryPointCommands.Add([pscustomobject]@{ file = 'npm'; arguments = @($args); cwd = (Get-Location).Path })
    $global:LASTEXITCODE = $global:WinnowEntryPointNpmExit
}
function node {
    $global:WinnowEntryPointCommands.Add([pscustomobject]@{
        file = 'node'; arguments = @($args); cwd = (Get-Location).Path
        backend = $env:WINNOW_BACKEND_PATH; activation = $env:WINNOW_ACTIVATION_HELPER_PATH; update = $env:WINNOW_UPDATE_HELPER_PATH
    })
    $global:LASTEXITCODE = $global:WinnowEntryPointNodeExit
}
$saved = @{}
foreach ($name in @('WINNOW_BACKEND_PATH', 'WINNOW_ACTIVATION_HELPER_PATH', 'WINNOW_UPDATE_HELPER_PATH')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, "original-$name")
}
try {
    Copy-Item -LiteralPath (Join-Path $repo 'Build.ps1'), (Join-Path $repo 'Run.ps1') -Destination $root
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Publish.ps1') -Destination (Join-Path $root 'packaging')
    @'
param($Runtime, $Version, $Commit, $OutputDirectory, [string[]]$ExtraProperties = @())
if ($Version -eq '0.2.0-failure') { throw 'Primary publisher failed.' }
[pscustomobject]@{ runtime=$Runtime; version=$Version; commit=$Commit; output=$OutputDirectory; properties=$ExtraProperties }
'@ | Set-Content -LiteralPath (Join-Path $root 'packaging/Publish-Electron.ps1')
    $output = Join-Path $root 'literal output; $notCode'
    $identity = & (Join-Path $root 'packaging/Publish.ps1') -Runtime win-x64 -Version 0.2.0-beta.9 -Commit ('a' * 40) -OutputDirectory $output -ExtraProperties @('-p:One=1', '-p:Two=two words')
    Assert-True ($identity.runtime -ceq 'win-x64' -and $identity.version -ceq '0.2.0-beta.9' -and $identity.commit -ceq ('a' * 40) -and
        $identity.output -ceq $output -and ($identity.properties -join '|') -ceq '-p:One=1|-p:Two=two words') 'Publish did not forward exact public arguments.'
    Assert-Failure { & (Join-Path $root 'packaging/Publish.ps1') -Runtime win-x64 -Version 0.2.0-failure -Commit ('a' * 40) -OutputDirectory $output } 'Primary publisher failed.'
    $default = & (Join-Path $root 'packaging/Publish.ps1') -Runtime linux-x64 -Version 0.2.0-dev -Commit ('b' * 40) -OutputDirectory $output
    Assert-True ($default.properties.Count -eq 0 -and $default.runtime -ceq 'linux-x64') 'Publish changed its optional properties default.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Publish-Avalonia.ps1') -Raw).Contains('src/Winnow.App/Winnow.App.csproj')) 'Explicit legacy publisher lost the reference frontend.'

    $artifacts = Join-Path $root 'artifacts with spaces'
    & (Join-Path $root 'Build.ps1') -Configuration Release -ArtifactsPath $artifacts
    Assert-True (($global:WinnowEntryPointCommands.file -join ',') -ceq 'dotnet,dotnet,npm') 'Default build did not build both companions and the frontend.'
    Assert-True ($global:WinnowEntryPointCommands[0].arguments -contains (Join-Path $root 'src/Winnow.Backend/Winnow.Backend.csproj') -and
        $global:WinnowEntryPointCommands[1].arguments -contains (Join-Path $root 'src/Winnow.Update.Helper/Winnow.Update.Helper.csproj') -and
        $global:WinnowEntryPointCommands[0].arguments -contains $artifacts -and $global:WinnowEntryPointCommands[0].arguments -contains 'Release') 'Build changed project, configuration, or artifact path.'
    Assert-True (($global:WinnowEntryPointCommands[2].arguments -join ' ') -ceq 'run build' -and
        $global:WinnowEntryPointCommands[2].cwd -ceq (Join-Path $root 'src/Winnow.Electron')) 'Frontend build ran outside its package.'
    $global:WinnowEntryPointCommands.Clear()
    $global:WinnowEntryPointDotnetExit = 7
    Assert-Failure { & (Join-Path $root 'Build.ps1') -ArtifactsPath $artifacts } 'Winnow.Backend build failed (7).'
    Assert-True ($global:WinnowEntryPointCommands.Count -eq 1) 'Failed companion build continued into later steps.'
    $global:WinnowEntryPointDotnetExit = 0
    $suffix = if ($IsWindows) { '.exe' } else { '' }
    foreach ($project in @('Winnow.Backend', 'Winnow.Update.Helper')) {
        $path = Join-Path $artifacts "bin/$project/debug/$project$suffix"
        $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
        [IO.File]::WriteAllText($path, 'test-owned placeholder; never executed')
    }
    $global:WinnowEntryPointCommands.Clear()
    $data = Join-Path $root 'throwaway library'
    & (Join-Path $root 'Run.ps1') -ArtifactsPath $artifacts -DataDirectory $data -SeedSample -NoSync -ApplicationArguments @('--fullscreen', '--uri', 'winnow://show')
    Assert-True (($global:WinnowEntryPointCommands.file -join ',') -ceq 'dotnet,dotnet,node') 'Development run did not build companions then use Vite.'
    $launch = $global:WinnowEntryPointCommands[2]
    Assert-True (($launch.arguments -join '|') -ceq "node_modules/electron-vite/bin/electron-vite.js|dev|--|--data-dir|$data|--seed-sample|--no-sync|--fullscreen|--uri|winnow://show") 'Run did not forward literal product arguments.'
    Assert-True ($launch.backend -ceq (Join-Path $artifacts "bin/Winnow.Backend/debug/Winnow.Backend$suffix") -and
        $launch.activation -ceq $launch.backend -and $launch.update -ceq (Join-Path $artifacts "bin/Winnow.Update.Helper/debug/Winnow.Update.Helper$suffix")) 'Run used unrelated companion outputs.'
    $global:WinnowEntryPointCommands.Clear()
    $global:WinnowEntryPointNodeExit = 9
    Assert-Failure { & (Join-Path $root 'Run.ps1') -Preview -ArtifactsPath $artifacts } 'Electron exited with code 9.'
    Assert-True (($global:WinnowEntryPointCommands.file -join ',') -ceq 'dotnet,dotnet,npm,node' -and
        ($global:WinnowEntryPointCommands[3].arguments -join '|') -ceq 'node_modules/electron-vite/bin/electron-vite.js|preview|--skipBuild|--') 'Preview did not build first or forced an unrequested data override.'
    foreach ($name in $saved.Keys) {
        Assert-True ([Environment]::GetEnvironmentVariable($name) -ceq "original-$name") 'Run leaked companion environment overrides.'
    }
    Write-Output "Passed $script:checks primary entry-point contracts; no build, package, or application process was executed."
}
finally {
    foreach ($name in @('WinnowEntryPointCommands', 'WinnowEntryPointNodeExit', 'WinnowEntryPointDotnetExit', 'WinnowEntryPointNpmExit')) {
        Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
    }
    if (-not $hadExitCode) { Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
    else { $global:LASTEXITCODE = $initialExitCode }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    $resolved = [IO.Path]::GetFullPath($root)
    if ([IO.Path]::GetFileName($resolved) -notlike 'Winnow-entrypoints-*' -or
        [IO.Path]::GetDirectoryName($resolved) -cne [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))) {
        throw 'Unsafe disposable entry-point test cleanup.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
