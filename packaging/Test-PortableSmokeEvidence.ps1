[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Portable-SmokeEvidence.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('Winnow-portable-evidence-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$script:checks = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Assert-Refused([scriptblock]$Action) {
    $refused = $false
    try { & $Action | Out-Null } catch { $refused = $true }
    Assert-True $refused 'Unsafe or malformed process identity was accepted.'
}
try {
    if ($IsLinux) {
        $self = Get-PortableLinuxProcessIdentity $PID
        $again = Get-PortableLinuxProcessIdentity $PID
        Assert-True ($self.processId -eq $PID -and $self.startTicks -eq $again.startTicks -and
            $self.executable -ceq [Environment]::ProcessPath) 'Actual Linux procfs identity is inconsistent.'
    }
    $executable = '/tmp/isolated portable/Winnow'
    $fields = @('S', '17') + @('0') * 17 + @('8123', '0')
    $proc = ConvertFrom-PortableProcStat ('7280 (Winnow (main) title) ' + ($fields -join ' ')) $executable
    Assert-True ($proc.processId -eq 7280 -and $proc.parentId -eq 17 -and $proc.startTicks -eq 8123 -and
        $proc.executable -ceq $executable) 'Proc stat fields or executable changed with a Chromium process title.'
    Assert-Refused { ConvertFrom-PortableProcStat '7280 broken stat' $executable }
    Assert-Refused { ConvertFrom-PortableProcStat '7280 (title) S 17 0' $executable }

    $record = [Text.Encoding]::UTF8.GetBytes("7280`n639265177578123456")
    $parsed = Read-PortableChildRecord $record
    Assert-True ($parsed.processId -eq 7280 -and $parsed.helperUtcTicks -eq 639265177578123456) 'Child record lost Int64 precision.'
    foreach ($invalid in @("0`n1", "-1`n1", "7280`n0", "7280`n1`nextra", "2147483648`n1", "7280`n9223372036854775808")) {
        Assert-Refused { Read-PortableChildRecord ([Text.Encoding]::UTF8.GetBytes($invalid)) }
    }

    $path = Join-Path $root 'child-process'
    [IO.File]::WriteAllBytes($path, $record)
    $helper = [pscustomobject]@{ processId = 17; startTicks = [ulong]7000; executable = '/tmp/owned/Winnow.Update.Helper' }
    $script:processIdentities = @{ 17 = $helper; 7280 = $proc }
    function Get-PortableLinuxProcessIdentity([int]$ProcessId) { return $script:processIdentities[$ProcessId] }
    $witness = Get-PortableReplacementWitness $path $helper $executable
    Assert-True ($null -ne $witness -and $witness.startTicks -ceq '8123' -and
        $witness.recordBase64 -ceq [Convert]::ToBase64String($record)) 'Owned live helper failed to witness exact child bytes and kernel birth.'
    Assert-PortableReplacementIdentity $witness $record $proc $executable
    $script:checks++

    # Independent UTC estimates deliberately differ; acceptance depends on the witnessed
    # kernel birth, never an absolute-time tolerance or Chromium's mutable title/argv.
    $proc | Add-Member NoteProperty calculatedUtcTicks 639265177578109876
    Assert-PortableReplacementIdentity $witness $record $proc $executable
    $script:checks++
    Assert-Refused { Assert-PortableReplacementIdentity $null $record $proc $executable }
    Assert-Refused { Assert-PortableReplacementIdentity $witness $record $null $executable }
    Assert-Refused { Assert-PortableReplacementIdentity $witness ([Text.Encoding]::UTF8.GetBytes("7280`n639265177578123457")) $proc $executable }
    Assert-Refused { Assert-PortableReplacementIdentity $witness ([Text.Encoding]::UTF8.GetBytes("7281`n639265177578123456")) $proc $executable }
    Assert-Refused { Assert-PortableReplacementIdentity $witness $record $proc '/tmp/isolated portable/winnow' }
    $proc.processId = 7281
    Assert-Refused { Assert-PortableReplacementIdentity $witness $record $proc $executable }
    $proc.processId = 7280
    $proc.startTicks++
    Assert-Refused { Assert-PortableReplacementIdentity $witness $record $proc $executable }
    $proc.startTicks--
    $proc.executable = '/tmp/another/Winnow'
    Assert-Refused { Assert-PortableReplacementIdentity $witness $record $proc $executable }
    Assert-True ($null -eq (Get-PortableReplacementWitness $path $helper $executable)) 'Wrong executable was witnessed.'
    $proc.executable = $executable
    $proc.parentId = 1
    Assert-True ($null -eq (Get-PortableReplacementWitness $path $helper $executable)) 'Already orphaned child was witnessed.'
    $proc.parentId = 17
    $script:processIdentities[17] = [pscustomobject]@{ processId = 17; startTicks = [ulong]7001; executable = $helper.executable }
    Assert-True ($null -eq (Get-PortableReplacementWitness $path $helper $executable)) 'Reused helper PID was witnessed.'
    $script:processIdentities[17] = $null
    Assert-True ($null -eq (Get-PortableReplacementWitness $path $helper $executable)) 'Absent helper was witnessed.'
    $script:processIdentities[17] = [pscustomobject]@{ processId = 17; startTicks = [ulong]7000; executable = '/tmp/unrelated/helper' }
    Assert-True ($null -eq (Get-PortableReplacementWitness $path $helper $executable)) 'Wrong helper executable was witnessed.'
    [IO.File]::WriteAllText($path, "7280`n")
    Assert-True ($null -eq (Get-PortableReplacementWitness $path $helper $executable)) 'Partial child record was witnessed.'

    $dataDirectories = @((Join-Path $root 'external/user-data'), (Join-Path $root 'internal/portable/user-data'))
    foreach ($data in $dataDirectories) {
        foreach ($relative in @('logs/backend.log', 'logs/diagnostic.log', 'logs/startup-failure.log',
            'electron-userdata/chromium/Local Storage/leveldb/000003.log', 'backend/endpoint.json', 'webview/profile.log')) {
            $file = Join-Path $data $relative
            $null = New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force
            [IO.File]::WriteAllText($file, $relative)
        }
    }
    $workspace = Join-Path $root 'external/.portable.winnow-update'
    $null = New-Item -ItemType Directory -Path $workspace
    [IO.File]::WriteAllBytes((Join-Path $workspace 'child-process'), $record)
    [IO.File]::WriteAllText((Join-Path $workspace 'journal.json'), '{"Phase":5}')
    $evidence = Join-Path $root 'evidence'
    Copy-PortableSmokeDiagnostics $root $dataDirectories $evidence
    $copied = @(Get-ChildItem -LiteralPath $evidence -File)
    Assert-True ($copied.Count -eq 8) 'Diagnostics did not copy exactly six product logs and two updater identity records.'
    Assert-True (@($copied | Where-Object Name -match 'chromium|leveldb|endpoint|webview').Count -eq 0) 'Profile or endpoint persistence escaped into diagnostics.'
    Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $evidence 'external-child-process'))) -ceq
        [Convert]::ToBase64String($record)) 'Archived child record bytes changed.'

    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-PortableUpgrade.ps1'), [ref]$null, [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'Portable smoke script does not parse.'
    $stop = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Stop-ReplacedApplication' }, $true)
    Assert-True ($stop.Extent.Text.Contains('$child.StartTime.ToUniversalTime().Ticks -ne $identity.helperUtcTicks')) 'Windows exact UTC identity check was removed.'
    Assert-True ($stop.Extent.Text.Contains('Assert-PortableReplacementIdentity')) 'Linux cleanup does not consume the witness.'
    Write-Output "Passed $script:checks portable process identity and diagnostic contracts; no process was launched or stopped."
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if ([IO.Path]::GetFileName($resolved) -notlike 'Winnow-portable-evidence-*' -or
        [IO.Path]::GetDirectoryName($resolved) -cne [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))) {
        throw 'Unsafe disposable evidence cleanup path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
