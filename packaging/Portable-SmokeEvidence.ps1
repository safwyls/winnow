# Linux Process.StartTime converts kernel ticks with a per-runtime estimate of boot UTC.
# Compare kernel identities instead: capture the child while its exact owned helper lives.
# https://github.com/dotnet/runtime/blob/v10.0.0/src/native/libs/System.Native/pal_time.c#L84-L100
function Read-PortableChildRecord([byte[]]$Bytes) {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    $match = [regex]::Match($text, '\A([1-9][0-9]*)\r?\n([1-9][0-9]*)(?:\r?\n)?\z')
    if (-not $match.Success) { throw 'The updated application process record is invalid.' }
    [pscustomobject]@{ processId = [int]$match.Groups[1].Value; helperUtcTicks = [long]$match.Groups[2].Value }
}

function ConvertFrom-PortableProcStat([string]$Stat, [string]$Executable) {
    # comm may contain spaces and parentheses; the remaining numeric fields start after its last ')'.
    $match = [regex]::Match($Stat, '\A([1-9][0-9]*) \(.*\) (.+)\s*\z', [Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) { throw 'Invalid Linux process stat.' }
    $fields = $match.Groups[2].Value.Trim() -split '\s+'
    if ($fields.Count -lt 20 -or $fields[1] -notmatch '^[0-9]+$' -or $fields[19] -notmatch '^[0-9]+$') {
        throw 'Linux process stat has no exact parent/start identity.'
    }
    [pscustomobject]@{
        processId = [int]$match.Groups[1].Value; parentId = [int]$fields[1]
        startTicks = [ulong]$fields[19]; executable = $Executable
    }
}

function Get-PortableLinuxProcessIdentity([int]$ProcessId) {
    try {
        $directory = "/proc/$ProcessId"
        $first = ConvertFrom-PortableProcStat ([IO.File]::ReadAllText("$directory/stat")) ''
        $executable = [IO.File]::ResolveLinkTarget("$directory/exe", $false).FullName
        $second = ConvertFrom-PortableProcStat ([IO.File]::ReadAllText("$directory/stat")) $executable
        if ($first.processId -ne $ProcessId -or $second.processId -ne $ProcessId -or $first.startTicks -ne $second.startTicks) {
            throw 'Linux process identity changed while it was read.'
        }
        return $second
    }
    catch [IO.FileNotFoundException] { return $null }
    catch [IO.DirectoryNotFoundException] { return $null }
}

function Get-PortableReplacementWitness([string]$ProcessRecord, $HelperIdentity, [string]$ExpectedExecutable) {
    if (-not (Test-Path -LiteralPath $ProcessRecord -PathType Leaf)) { return $null }
    $bytes = [IO.File]::ReadAllBytes($ProcessRecord)
    # File.WriteAllText may still be publishing the two lines. Cleanup always parses strictly.
    try { $record = Read-PortableChildRecord $bytes } catch { return $null }
    $helperNow = Get-PortableLinuxProcessIdentity $HelperIdentity.processId
    if ($null -eq $helperNow -or $helperNow.startTicks -ne $HelperIdentity.startTicks -or
        $helperNow.executable -cne $HelperIdentity.executable) { return $null }
    $child = Get-PortableLinuxProcessIdentity $record.processId
    if ($null -eq $child -or $child.parentId -ne $HelperIdentity.processId -or
        $child.executable -cne $ExpectedExecutable) { return $null }
    $encoded = [Convert]::ToBase64String($bytes)
    if ($encoded -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($ProcessRecord))) { return $null }
    [pscustomobject]@{
        recordBase64 = $encoded; helperUtcTicks = $record.helperUtcTicks.ToString()
        processId = $child.processId; startTicks = $child.startTicks.ToString(); executable = $child.executable
        helper = $HelperIdentity
    }
}

function Assert-PortableReplacementIdentity($Witness, [byte[]]$RecordBytes, $Observed, [string]$ExpectedExecutable) {
    $record = Read-PortableChildRecord $RecordBytes
    if ($null -eq $Witness -or $null -eq $Observed -or
        $Witness.recordBase64 -cne [Convert]::ToBase64String($RecordBytes) -or
        $record.processId -ne $Witness.processId -or $Observed.processId -ne $Witness.processId -or
        $Observed.startTicks.ToString() -cne $Witness.startTicks -or
        $Witness.executable -cne $ExpectedExecutable -or $Observed.executable -cne $ExpectedExecutable) {
        throw 'The updated application process identity changed or was not witnessed; refusing cleanup.'
    }
}

function Copy-PortableSmokeDiagnostics([string]$WorkspaceRoot, [string[]]$DataDirectories, [string]$Destination) {
    $null = New-Item -ItemType Directory -Path $Destination -Force
    foreach ($scenario in Get-ChildItem -LiteralPath $WorkspaceRoot -Directory) {
        foreach ($name in @('journal.json', 'child-process')) {
            $path = Join-Path $scenario.FullName ".portable.winnow-update/$name"
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Copy-Item -LiteralPath $path -Destination (Join-Path $Destination ($scenario.Name + '-' + $name))
            }
        }
    }
    foreach ($data in $DataDirectories) {
        $logs = Join-Path $data 'logs'
        if (-not (Test-Path -LiteralPath $logs -PathType Container)) { continue }
        # Product diagnostics only. Chromium LevelDB/WAL files are persistent profile data.
        foreach ($file in Get-ChildItem -LiteralPath $logs -File -Filter '*.log') {
            $name = [IO.Path]::GetRelativePath($WorkspaceRoot, $file.FullName).Replace('/', '_').Replace('\', '_')
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $name)
        }
    }
}
