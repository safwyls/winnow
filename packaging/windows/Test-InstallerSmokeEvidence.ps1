[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-SmokeEvidence.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-installer-evidence-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
function Assert-Refused([scriptblock]$Action) {
    $refused = $false
    try { & $Action 2>$null | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Expected the invalid evidence input to be refused.' }
    $global:LASTEXITCODE = 0
}
try {
    $publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
    $destination = Join-Path $root 'embedded-helper.ps1'
    $before = @([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object FullName)
    $evidence = Export-InstalledUpdateHelper $publish $destination
    $after = @([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object FullName)
    if (@($after | Where-Object { $_ -notin $before -and $_ -match '^Winnow(?:\.|,)' }).Count -ne 0) {
        throw 'Resource extraction loaded Winnow managed code.'
    }
    $assembly = Join-Path $publish $evidence.assembly
    $exclusive = [IO.File]::Open($assembly, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $exclusive.Dispose()
    if ($evidence.assemblySha256 -cne (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant() -or
        $evidence.helperSha256 -cne (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant()) {
        throw 'Resource extraction changed the assembly or reported inconsistent helper evidence.'
    }
    $errors = $null
    $helperAst = [Management.Automation.Language.Parser]::ParseFile($destination, [ref]$null, [ref]$errors)
    if ($errors.Count) { throw 'Extracted resource is not a PowerShell script.' }
    $restarts = @($helperAst.FindAll({ param($node)
        $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Start-Process' -and
        $node.Extent.Text.Contains('-FilePath $handoff.Executable')
    }, $true))
    if ($restarts.Count -ne 1) { throw 'Installed helper did not contain its actual unique frontend restart.' }
    Write-Host 'Real packaged helper extracted with exact hashes, no assembly execution and no retained binary lock.'

    $legacy = Join-Path $root 'legacy'
    $null = New-Item -ItemType Directory -Path $legacy
    Copy-Item -LiteralPath $assembly -Destination (Join-Path $legacy 'Winnow.dll')
    Copy-Item -LiteralPath (Join-Path $publish 'release-info.json') -Destination $legacy
    $legacyEvidence = Export-InstalledUpdateHelper $legacy (Join-Path $root 'legacy-helper.ps1')
    if ($legacyEvidence.assembly -cne 'Winnow.dll' -or $legacyEvidence.helperSha256 -cne $evidence.helperSha256) {
        throw 'The legacy root-assembly path did not retain exact embedded resource bytes.'
    }
    Write-Host 'Legacy root-assembly selection retains identical embedded bytes.'

    Copy-Item -LiteralPath (Join-Path $publish 'backend/Winnow.Backend.dll') -Destination (Join-Path $legacy 'Winnow.dll') -Force
    Assert-Refused { Export-InstalledUpdateHelper $legacy (Join-Path $root 'missing.ps1') }
    [IO.File]::WriteAllText((Join-Path $legacy 'Winnow.dll'), 'invalid assembly')
    Assert-Refused { Export-InstalledUpdateHelper $legacy (Join-Path $root 'invalid.ps1') }
    if ((Test-Path -LiteralPath (Join-Path $root 'missing.ps1')) -or (Test-Path -LiteralPath (Join-Path $root 'invalid.ps1'))) {
        throw 'Refused resource extraction wrote a helper.'
    }
    Write-Host 'Missing embedded resource and corrupt PE are refused without emitting a replacement helper.'

    $database = Join-Path $root 'fixture.db'
    $seed = @'
import sqlite3, sys
with sqlite3.connect(sys.argv[1]) as c:
    c.executescript('CREATE TABLE works(id INTEGER,name TEXT); CREATE TABLE releases(id INTEGER,work_id INTEGER,name TEXT); CREATE TABLE ownerships(id INTEGER,release_id INTEGER,store TEXT,account_ref TEXT,acquired_at TEXT,license_type TEXT,price_paid_cents INTEGER);')
'@
    & python -c $seed $database
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the isolated SQLite evidence fixture.' }
    Initialize-InstalledSmokeLibrary $database
    $initial = Read-InstalledLibraryEvidence $database
    $facts = $initial | ConvertFrom-Json
    if ($facts.works[0][0] -ne -159 -or $facts.releases[0][1] -ne -159 -or
        $facts.ownerships[0][1] -ne -159 -or $facts.ownerships[0][3] -cne '11111' -or $facts.ownerships[0][6] -ne 500) {
        throw 'The explicit release-safe seed lost its linked work/release/ownership facts.'
    }
    if ((Read-InstalledLibraryEvidence $database) -cne $initial) { throw 'Unchanged library evidence was not stable.' }
    & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("UPDATE ownerships SET price_paid_cents=600"); c.commit(); c.close()' $database
    if ($LASTEXITCODE -ne 0 -or (Read-InstalledLibraryEvidence $database) -ceq $initial) { throw 'Changed ownership fact escaped the library comparison.' }
    & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("DELETE FROM works"); c.commit(); c.close()' $database
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare emptied library evidence.' }
    Assert-Refused { Read-InstalledLibraryEvidence $database }
    $missing = Join-Path $root 'missing.db'
    Assert-Refused { Read-InstalledLibraryEvidence $missing }
    if (Test-Path -LiteralPath $missing) { throw 'Read-only evidence created a missing database.' }
    Write-Host 'SQLite evidence preserves exact identities/facts and refuses empty or missing libraries without creation.'

    $portableAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '../Test-PortableUpgrade.ps1'), [ref]$null, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    $preserveFunction = $portableAst.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-PreservedPortableFiles'
    }, $true)
    Invoke-Expression $preserveFunction.Extent.Text
    $sentinels = @('covers/a', 'themes/a', 'webview/a', 'credentials')
    foreach ($relative in $sentinels) {
        $path = Join-Path $root $relative
        $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
        [IO.File]::WriteAllText($path, 'preserve these user-owned bytes')
    }
    Assert-PreservedPortableFiles $root $sentinels
    foreach ($relative in $sentinels) {
        $path = Join-Path $root $relative
        [IO.File]::WriteAllText($path, 'changed')
        Assert-Refused { Assert-PreservedPortableFiles $root $sentinels }
        [IO.File]::WriteAllText($path, 'preserve these user-owned bytes')
    }
    $calls = @($portableAst.FindAll({ param($node)
        $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Assert-PreservedPortableFiles'
    }, $true))
    if ($calls.Count -ne 3) { throw 'Successful replacement and both recovery branches must each check preserved files.' }
    Write-Host 'Every preserved file is checked and both recovery branches invoke the same byte-preservation assertion.'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $prefix = Join-Path ([IO.Path]::GetTempPath()) 'Winnow-installer-evidence-'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $resolved -notmatch 'Winnow-installer-evidence-[0-9a-f]{32}$') {
        throw 'Evidence cleanup escaped its temporary directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
