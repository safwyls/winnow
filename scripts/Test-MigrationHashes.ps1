$ErrorActionPreference = 'Stop'
$verify = Join-Path $PSScriptRoot 'Verify-Migrations.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('winnow-hash-test-' + [Guid]::NewGuid().ToString('N'))
$directory = Join-Path $scratch 'src/Winnow.Data/Migrations'
[IO.Directory]::CreateDirectory($directory) | Out-Null

function Set-Fixture([string] $sql) {
    [IO.File]::WriteAllText((Join-Path $directory '0001_initial.sql'), $sql)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql.Replace("`r`n", "`n")))).ToLowerInvariant()
    @{ '0001_initial.sql' = $hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'hashes.json')
}

function Expect-Rejection([string] $baseline) {
    $rejected = $false
    try { & $verify -RepositoryRoot $scratch -BaselineRef $baseline | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw 'An invalid migration change passed verification.' }
}

try {
    Set-Fixture "SELECT 1;`n"
    & $verify -RepositoryRoot $scratch
    [IO.File]::WriteAllText((Join-Path $directory '0001_initial.sql'), "SELECT 1;`r`n")
    & $verify -RepositoryRoot $scratch
    [IO.File]::WriteAllText((Join-Path $directory '0001_initial.sql'), "SELECT 2;`n")
    Expect-Rejection
    Set-Fixture "SELECT 1;`n"
    [IO.File]::WriteAllText((Join-Path $directory '0002_unrecorded.sql'), 'SELECT 2;')
    Expect-Rejection
    Remove-Item -LiteralPath (Join-Path $directory '0002_unrecorded.sql')
    & git -C $scratch init --quiet
    & git -C $scratch add .
    & git -C $scratch -c user.name=Test -c user.email=test@example.invalid commit --quiet -m baseline
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated git test baseline.' }
    & $verify -RepositoryRoot $scratch -BaselineRef HEAD
    Set-Fixture "SELECT 2;`n"
    Expect-Rejection HEAD
    Remove-Item -LiteralPath (Join-Path $directory '0001_initial.sql')
    Expect-Rejection
    Write-Output 'Migration checks passed: matching hashes, CRLF, modified SQL, unrecorded SQL, changed baseline hash, missing SQL.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
