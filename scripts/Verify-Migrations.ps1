[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string] $BaselineRef
)

$ErrorActionPreference = 'Stop'
$manifestRelative = 'src/Winnow.Data/Migrations/hashes.json'
$migrationDirectory = Join-Path $RepositoryRoot 'src/Winnow.Data/Migrations'
$manifest = Get-Content -LiteralPath (Join-Path $RepositoryRoot $manifestRelative) -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.Count -eq 0) { throw 'The migration hash manifest is empty.' }

$files = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*.sql' -File -Recurse)
if ($files.Count -ne $manifest.Count) { throw 'Migration files and recorded hashes differ in count.' }
foreach ($file in $files) {
    $name = [IO.Path]::GetRelativePath($migrationDirectory, $file.FullName).Replace('\', '/')
    if (!$manifest.ContainsKey($name)) { throw "Migration has no recorded hash: $name" }
    # Git checkouts may use CRLF or LF; all other text, including trailing whitespace, is significant.
    $sql = [IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n")
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql))).ToLowerInvariant()
    if ($hash -cne $manifest[$name]) { throw "Migration content changed: $name" }
}

if ($BaselineRef) {
    # A hash updated together with an old migration must not make an edit look legitimate.
    $baselineJson = & git -C $RepositoryRoot show "${BaselineRef}:$manifestRelative" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $baseline = ($baselineJson -join "`n") | ConvertFrom-Json -AsHashtable
        foreach ($name in $baseline.Keys) {
            if (!$manifest.ContainsKey($name) -or $manifest[$name] -cne $baseline[$name]) {
                throw "Previously recorded migration hash changed or was removed: $name"
            }
        }
    }
    else {
        # Bootstrap the first manifest against the actual SQL files in the previous revision.
        $priorFiles = & git -C $RepositoryRoot ls-tree -r --name-only $BaselineRef -- 'src/Winnow.Data/Migrations'
        if ($LASTEXITCODE -ne 0) { throw "Cannot read migration baseline: $BaselineRef" }
        foreach ($path in $priorFiles | Where-Object { $_.EndsWith('.sql', [StringComparison]::Ordinal) }) {
            $name = $path.Substring('src/Winnow.Data/Migrations/'.Length)
            # git show text output omits the final newline; recover it without shell redirection.
            $start = [Diagnostics.ProcessStartInfo]::new('git')
            $start.ArgumentList.Add('-C'); $start.ArgumentList.Add($RepositoryRoot)
            $start.ArgumentList.Add('show'); $start.ArgumentList.Add("${BaselineRef}:$path")
            $start.RedirectStandardOutput = $true
            $start.UseShellExecute = $false
            $process = [Diagnostics.Process]::Start($start)
            try {
                $sql = $process.StandardOutput.ReadToEnd().Replace("`r`n", "`n")
                $process.WaitForExit()
                if ($process.ExitCode -ne 0) { throw "Cannot read baseline migration: $name" }
            }
            finally { $process.Dispose() }
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql))).ToLowerInvariant()
            if (!$manifest.ContainsKey($name) -or $manifest[$name] -cne $hash) {
                throw "Previously shipped migration changed or was removed: $name"
            }
        }
    }
}

Write-Output "Verified $($files.Count) migration hashes."
