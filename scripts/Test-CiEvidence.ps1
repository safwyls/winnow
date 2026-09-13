[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CiEvidence.psm1') -Force
$script:checks = 0
function Assert-Policy([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function New-Fixture {
    $commit = 'a' * 40
    $current = @{
        repository = 'fixture/winnow'; commit = $commit; tree = 'b' * 40
        platform = 'windows'; sdk = '10.0.100'; imageOS = 'win25'; imageVersion = '20260101.1'
        dependencies = 'c' * 64; runId = '200'; event = 'push'; forceFull = $false
    }
    $evidence = $current.Clone()
    $evidence.schema = 1; $evidence.kind = 'full'; $evidence.runId = '100'; $evidence.runAttempt = 1
    return @{
        Current = $current; Evidence = $evidence
        Run = @{
            id = 100; run_attempt = 1; status = 'completed'; conclusion = 'success'
            repository = @{ full_name = 'fixture/winnow' }; head_repository = @{ full_name = 'fixture/winnow' }
            path = '.github/workflows/ci.yml'; event = 'push'; head_sha = $commit
            created_at = [DateTimeOffset]::UtcNow.AddHours(-1).ToString('o'); html_url = 'https://github.com/fixture/winnow/actions/runs/100'
        }
        Jobs = @(
            @{ name = 'Windows build, tests and migration integrity'; conclusion = 'success' }
            @{ name = 'Linux native and Proton-environment session smoke tests'; conclusion = 'success' }
        )
        PullRequest = $null; TestedCommit = $null
    }
}
function New-PrFixture {
    $f = New-Fixture
    $f.Run.event = 'pull_request'; $f.Run.head_sha = 'd' * 40
    $f.Evidence.event = 'pull_request'; $f.Evidence.commit = 'e' * 40
    $f.Evidence.pullRequest = 12; $f.Evidence.headCommit = $f.Run.head_sha; $f.Evidence.baseCommit = 'f' * 40
    $f.PullRequest = @{
        number = 12; merged = $true; merge_commit_sha = $f.Current.commit
        base = @{ ref = 'main'; repo = @{ full_name = 'fixture/winnow' }; sha = $f.Evidence.baseCommit }
        head = @{ repo = @{ full_name = 'fixture/winnow' }; sha = $f.Run.head_sha }
    }
    $f.TestedCommit = @{
        sha = $f.Evidence.commit; tree = @{ sha = $f.Evidence.tree }
        parents = @(@{ sha = $f.Evidence.baseCommit }, @{ sha = $f.Evidence.headCommit })
    }
    return $f
}
$f = New-Fixture
Assert-Policy (Test-CiEvidence @f) 'Exact-commit successful evidence should match.'
$f.Run.path = '.github/workflows/release.yml'
$f.Jobs | ForEach-Object { $_.name = 'verify / ' + $_.name }
Assert-Policy (Test-CiEvidence @f) 'Successful reusable release jobs should match.'
$f = New-PrFixture
Assert-Policy (Test-CiEvidence @f) 'Verified PR merge tree should map to the actual merged commit.'

$rejections = @{
    'failed run' = { param($f) $f.Run.conclusion = 'failure' }
    'incomplete run' = { param($f) $f.Run.status = 'in_progress' }
    'wrong repository' = { param($f) $f.Run.repository.full_name = 'other/repo' }
    'fork' = { param($f) $f.Run.head_repository.full_name = 'fork/winnow' }
    'wrong workflow' = { param($f) $f.Run.path = '.github/workflows/other.yml' }
    'self reference' = { param($f) $f.Current.runId = '100' }
    'old evidence' = { param($f) $f.Run.created_at = [DateTimeOffset]::UtcNow.AddHours(-25).ToString('o') }
    'future evidence' = { param($f) $f.Run.created_at = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o') }
    'missing platform' = { param($f) $f.Jobs = @($f.Jobs[0]) }
    'skipped platform' = { param($f) $f.Jobs[1].conclusion = 'skipped' }
    'failed platform' = { param($f) $f.Jobs[0].conclusion = 'failure' }
    'duplicate platform' = { param($f) $f.Jobs += $f.Jobs[0] }
    'wrong schema' = { param($f) $f.Evidence.schema = 2 }
    'reused evidence' = { param($f) $f.Evidence.kind = 'reused' }
    'wrong run' = { param($f) $f.Evidence.runId = 99 }
    'wrong attempt' = { param($f) $f.Evidence.runAttempt = 2 }
    'wrong source' = { param($f) $f.Evidence.tree = '0' * 40 }
    'different SDK' = { param($f) $f.Evidence.sdk = '10.0.101' }
    'different OS' = { param($f) $f.Evidence.platform = 'linux' }
    'different image' = { param($f) $f.Evidence.imageVersion = '20260102.1' }
    'different dependencies' = { param($f) $f.Evidence.dependencies = '0' * 64 }
    'missing metadata' = { param($f) $f.Current.imageVersion = '' }
    'wrong checkout' = { param($f) $f.Evidence.commit = '0' * 40 }
    'wrong run head' = { param($f) $f.Run.head_sha = '0' * 40 }
    'unsupported event' = { param($f) $f.Run.event = 'workflow_run' }
    'malformed evidence' = { param($f) $f.Evidence = @{} }
}
foreach ($case in $rejections.GetEnumerator()) {
    $f = New-Fixture; & $case.Value $f
    Assert-Policy (!(Test-CiEvidence @f)) "Accepted $($case.Key)."
}
$prRejections = @{
    'unmerged PR' = { param($f) $f.PullRequest.merged = $false }
    'different merge result' = { param($f) $f.PullRequest.merge_commit_sha = '0' * 40 }
    'different PR head' = { param($f) $f.PullRequest.head.sha = '0' * 40 }
    'different base' = { param($f) $f.PullRequest.base.sha = '0' * 40 }
    'wrong base branch' = { param($f) $f.PullRequest.base.ref = 'feature' }
    'wrong PR number' = { param($f) $f.PullRequest.number = 13 }
    'unverified tested tree' = { param($f) $f.TestedCommit.tree.sha = '0' * 40 }
    'unverified tested head' = { param($f) $f.TestedCommit.parents[1].sha = '0' * 40 }
    'not a merge checkout' = { param($f) $f.TestedCommit.parents = @($f.TestedCommit.parents[0]) }
}
foreach ($case in $prRejections.GetEnumerator()) {
    $f = New-PrFixture; & $case.Value $f
    Assert-Policy (!(Test-CiEvidence @f)) "Accepted $($case.Key)."
}

# Exercise the discovery path with the same API shapes used in Actions.
$f = New-PrFixture
$api = {
    param($path)
    switch -Wildcard ($path) {
        'actions/workflows/ci.yml/runs?*' { return @{ workflow_runs = @($f.Run) } }
        'actions/workflows/release.yml/runs?*' { return @{ workflow_runs = @() } }
        'actions/runs/100/artifacts?*' { return @{ artifacts = @(@{ id = 123; name = 'ci-evidence-windows-1'; expired = $false }) } }
        'actions/runs/100/attempts/1/jobs?*' { return @{ jobs = $f.Jobs } }
        'pulls/12' { return $f.PullRequest }
        'git/commits/*' { return $f.TestedCommit }
        default { throw "Unexpected API route: $path" }
    }
}
$read = { param($id) if ($id -ne 123) { throw 'Wrong artifact requested.' }; return $f.Evidence }
Assert-Policy ($null -ne (Find-CiEvidence $f.Current $api $read)) 'Discovery did not reuse valid merged PR evidence.'
$f.Current.event = 'pull_request'
Assert-Policy ($null -eq (Find-CiEvidence $f.Current { throw 'PR must not query API.' } $read)) 'PR reused evidence.'
$f.Current.event = 'push'; $f.Current.forceFull = $true
Assert-Policy ($null -eq (Find-CiEvidence $f.Current { throw 'Forced run must not query API.' } $read)) 'Forced full reused evidence.'
$f.Current.forceFull = $false
Assert-Policy ($null -eq (Find-CiEvidence $f.Current { throw 'API unavailable.' } $read)) 'API failure did not fall back.'
Assert-Policy ($null -eq (Find-CiEvidence $f.Current $api { throw 'Expired artifact.' })) 'Artifact failure did not fall back.'
Assert-Policy ($null -eq (Find-CiEvidence $f.Current { return @{ workflow_runs = @() } } $read)) 'Missing evidence did not fall back.'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('winnow-ci-evidence-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path (Join-Path $temp 'obj') -Force | Out-Null
    & git -C $temp init --quiet
    '<Project />' | Set-Content -LiteralPath (Join-Path $temp 'Fixture.csproj')
    & git -C $temp add Fixture.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare fingerprint fixture.' }
    $path = Join-Path $temp 'obj/project.assets.json'
    $assets = @{ libraries = @{ 'A/1.0' = @{ type = 'package'; sha512 = 'hash-a' }; 'Core/1.0' = @{ type = 'project' } }; targets = @{ net10 = @{ 'A/1.0' = @{ compile = @{ 'lib/a.dll' = @{} } } } } }
    $assets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path
    $hash = Get-CiDependencyHash $temp
    $assets.project = @{ restore = @{ packagesPath = '/different/cache' } }
    $assets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path
    Assert-Policy ($hash -ceq (Get-CiDependencyHash $temp)) 'Machine cache path changed dependency hash.'
    $assets.libraries['A/1.0'].sha512 = 'hash-b'
    $assets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path
    Assert-Policy ($hash -cne (Get-CiDependencyHash $temp)) 'Package content change did not change dependency hash.'
    $assets.libraries['A/1.0'].sha512 = 'hash-a'; $assets.targets.net10['A/1.0'].compile = @{ 'lib/b.dll' = @{} }
    $assets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path
    Assert-Policy ($hash -cne (Get-CiDependencyHash $temp)) 'Selected runtime/compile asset change did not change dependency hash.'

    # Exercise the entrypoint, ZIP parser and Actions outputs with API responses
    # supplied locally. No external requests or production credentials are used.
    & git -C $temp -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit fingerprint fixture.' }
    $f = New-Fixture
    $f.Current.commit = & git -C $temp rev-parse HEAD
    $f.Current.tree = & git -C $temp rev-parse 'HEAD^{tree}'
    $f.Current.sdk = & dotnet --version
    $f.Current.dependencies = Get-CiDependencyHash $temp
    $f.Evidence = $f.Current.Clone()
    $f.Evidence.schema = 1; $f.Evidence.kind = 'full'; $f.Evidence.runId = 100; $f.Evidence.runAttempt = 1
    $f.Run.head_sha = $f.Current.commit
    $testZip = Join-Path $temp 'evidence.zip'
    function Write-TestArchive([string] $Name, [string] $Content) {
        $file = [IO.File]::Create($testZip)
        $zip = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($Name).Open())
            try { $writer.Write($Content) } finally { $writer.Dispose() }
        }
        finally { $zip.Dispose(); $file.Dispose() }
    }
    function Invoke-RestMethod {
        param($Uri, $Headers, $TimeoutSec)
        return & $fixtureApi ([string]$Uri).Split('/repos/fixture/winnow/')[1]
    }
    function Invoke-WebRequest {
        param($Uri, $Headers, $OutFile, $TimeoutSec)
        if ($Uri -cne 'https://fixture.invalid/repos/fixture/winnow/actions/artifacts/123/zip') { throw 'Unexpected download URL.' }
        Copy-Item -LiteralPath $testZip -Destination $OutFile
    }
    $envValues = @{
        GITHUB_EVENT_NAME = 'push'; GITHUB_EVENT_PATH = (Join-Path $temp 'event.json')
        GITHUB_OUTPUT = (Join-Path $temp 'output.txt'); GITHUB_STEP_SUMMARY = (Join-Path $temp 'summary.txt')
        GITHUB_REPOSITORY = 'fixture/winnow'; GITHUB_RUN_ID = '200'; GITHUB_RUN_ATTEMPT = '1'
        GITHUB_API_URL = 'https://fixture.invalid'; GH_TOKEN = 'fixture'; CI_FORCE_FULL = 'false'
        ImageOS = $f.Current.imageOS; ImageVersion = $f.Current.imageVersion
    }
    $previousEnv = @{}
    $fixtureApi = $api
    try {
        foreach ($key in $envValues.Keys) {
            $previousEnv[$key] = [Environment]::GetEnvironmentVariable($key)
            [Environment]::SetEnvironmentVariable($key, $envValues[$key])
        }
        '{}' | Set-Content -LiteralPath $env:GITHUB_EVENT_PATH
        Write-TestArchive 'windows.json' ($f.Evidence | ConvertTo-Json -Depth 10)
        & (Join-Path $PSScriptRoot 'Resolve-CiEvidence.ps1') -Platform windows -RepositoryRoot $temp
        Assert-Policy ((Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw).Trim() -ceq 'reused=true') 'Entrypoint did not enable verified reuse.'
        Assert-Policy (!(Test-Path -LiteralPath (Join-Path $temp 'ci-evidence/windows.json'))) 'Reuse generated renewed evidence.'
        Assert-Policy ((Get-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Raw).Contains($f.Run.html_url)) 'Reuse summary did not link original run.'
        foreach ($badArchive in @(
            @{ name = '../windows.json'; content = '{}' },
            @{ name = 'windows.json'; content = 'invalid json' },
            @{ name = 'windows.json'; content = ('x' * 17000) }
        )) {
            Write-TestArchive $badArchive.name $badArchive.content
            Clear-Content -LiteralPath $env:GITHUB_OUTPUT
            & (Join-Path $PSScriptRoot 'Resolve-CiEvidence.ps1') -Platform windows -RepositoryRoot $temp
            Assert-Policy ((Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw).Trim() -ceq 'reused=false') 'Invalid archive did not fall back to full tests.'
        }
    }
    finally {
        foreach ($key in $previousEnv.Keys) { [Environment]::SetEnvironmentVariable($key, $previousEnv[$key]) }
        Remove-Item Function:Invoke-RestMethod, Function:Invoke-WebRequest
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temp)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "CI evidence policy passed ($script:checks checks)."
