Set-StrictMode -Version Latest

function ConvertTo-CanonicalValue($Value) {
    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in @($Value.Keys | Sort-Object -CaseSensitive)) {
            $result[$key] = ConvertTo-CanonicalValue $Value[$key]
        }
        return $result
    }
    if ($Value -is [array]) { return ,@($Value | ForEach-Object { ConvertTo-CanonicalValue $_ }) }
    return $Value
}

function Get-CiDependencyHash {
    param([string] $RepositoryRoot)
    $projects = @(& git -C $RepositoryRoot ls-files '*.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate restored projects.' }
    $records = [ordered]@{}
    foreach ($project in ($projects | Sort-Object -CaseSensitive)) {
        $projectDirectory = Split-Path $project
        $assetsPath = Join-Path $RepositoryRoot "$projectDirectory/obj/project.assets.json".TrimStart('/')
        if (!(Test-Path -LiteralPath $assetsPath)) { continue }
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
        $libraries = [ordered]@{}
        foreach ($key in ($assets.libraries.Keys | Sort-Object -CaseSensitive)) {
            $library = $assets.libraries[$key]
            if ($library.type -eq 'package' -and [string]::IsNullOrWhiteSpace($library['sha512'])) {
                throw "Restored package has no content hash: $key"
            }
            $libraries[$key] = @{ type = $library.type; sha512 = $library['sha512'] }
        }
        # Targets capture selected framework/runtime assets; library hashes capture
        # package contents. Machine-specific restore/cache paths are excluded.
        $records[$project] = @{ targets = $assets.targets; libraries = $libraries }
    }
    if ($records.Count -eq 0) { throw 'No restored project assets were found.' }
    $json = ConvertTo-CanonicalValue $records | ConvertTo-Json -Depth 100 -Compress
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
}

function Test-CiEvidence {
    param($Evidence, $Current, $Run, $Jobs, $PullRequest, $TestedCommit, [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow)
    try {
        if ($Run.status -cne 'completed' -or $Run.conclusion -cne 'success' -or
            $Run.repository.full_name -cne $Current.repository -or
            $Run.head_repository.full_name -cne $Current.repository -or
            $Run.path -cnotin @('.github/workflows/ci.yml', '.github/workflows/release.yml') -or
            [string]$Run.id -eq [string]$Current.runId) { return $false }
        $age = $Now - [DateTimeOffset]::Parse($Run.created_at)
        if ($age.TotalHours -gt 24 -or $age.TotalSeconds -lt 0) { return $false }
        foreach ($name in @('Windows build, tests and migration integrity', 'Linux native and Proton-environment session smoke tests')) {
            $matches = @($Jobs | Where-Object { $_.name -ceq $name -or $_.name.EndsWith(" / $name", [StringComparison]::Ordinal) })
            if ($matches.Count -ne 1 -or $matches[0].conclusion -cne 'success') { return $false }
        }
        if ($Evidence.schema -ne 1 -or $Evidence.kind -cne 'full' -or
            [string]$Evidence.runId -ne [string]$Run.id -or
            [int]$Evidence.runAttempt -ne [int]$Run.run_attempt) { return $false }
        foreach ($key in @('repository', 'tree', 'platform', 'sdk', 'imageOS', 'imageVersion', 'dependencies')) {
            if ([string]::IsNullOrWhiteSpace([string]$Current[$key]) -or $Evidence[$key] -cne $Current[$key]) { return $false }
        }
        if ($Run.event -ceq 'pull_request') {
            # A PR run's head_sha is the branch tip, not the checkout under test.
            # Bind its recorded merge checkout to GitHub's merged PR metadata.
            if ($Run.path -cne '.github/workflows/ci.yml' -or !$PullRequest.merged -or
                $PullRequest.base.repo.full_name -cne $Current.repository -or
                $PullRequest.head.repo.full_name -cne $Current.repository -or
                $PullRequest.base.ref -cne 'main' -or
                $PullRequest.head.sha -cne $Run.head_sha -or
                $PullRequest.merge_commit_sha -cne $Current.commit -or
                $Evidence.event -cne 'pull_request' -or
                [int]$Evidence.pullRequest -ne [int]$PullRequest.number -or
                $Evidence.headCommit -cne $Run.head_sha -or
                $Evidence.baseCommit -cne $PullRequest.base.sha -or
                $Evidence.commit -cnotmatch '^[0-9a-f]{40}$' -or
                $TestedCommit.sha -cne $Evidence.commit -or
                $TestedCommit.tree.sha -cne $Evidence.tree -or
                $TestedCommit.parents.Count -ne 2 -or
                $TestedCommit.parents[0].sha -cne $Evidence.baseCommit -or
                $TestedCommit.parents[1].sha -cne $Evidence.headCommit) { return $false }
        }
        elseif ($Run.event -cin @('push', 'workflow_dispatch')) {
            if ($Evidence.event -cne $Run.event -or $Run.head_sha -cne $Current.commit -or
                $Evidence.commit -cne $Current.commit) { return $false }
        }
        else { return $false }
        return $true
    }
    catch { return $false }
}

function Find-CiEvidence {
    param($Current, [scriptblock] $Api, [scriptblock] $ReadArtifact)
    # PR checks must always exercise the new code. Dispatching CI also permits
    # deliberately collecting fresh evidence rather than renewing a reused run.
    if ($Current.event -eq 'pull_request' -or $Current.forceFull) { return $null }
    try {
        $since = [Uri]::EscapeDataString('>=' + [DateTimeOffset]::UtcNow.AddHours(-24).ToString('yyyy-MM-ddTHH:mm:ssZ'))
        $runs = foreach ($workflow in @('ci.yml', 'release.yml')) {
            (& $Api "actions/workflows/$workflow/runs?status=success&per_page=30&created=$since").workflow_runs
        }
        foreach ($run in @($runs | Sort-Object created_at -Descending)) {
            if ([string]$run.id -eq [string]$Current.runId -or $run.repository.full_name -cne $Current.repository -or
                $run.head_repository.full_name -cne $Current.repository) { continue }
            if ($run.event -ne 'pull_request' -and $run.head_sha -cne $Current.commit) { continue }
            $artifacts = (& $Api "actions/runs/$($run.id)/artifacts?per_page=100").artifacts
            $name = "ci-evidence-$($Current.platform)-$($run.run_attempt)"
            $artifact = @($artifacts | Where-Object { $_.name -ceq $name -and !$_.expired })
            if ($artifact.Count -ne 1) { continue }
            $evidence = & $ReadArtifact $artifact[0].id
            if ($evidence.tree -cne $Current.tree) { continue }
            $jobs = (& $Api "actions/runs/$($run.id)/attempts/$($run.run_attempt)/jobs?per_page=100").jobs
            $pr = $null
            $testedCommit = $null
            if ($run.event -eq 'pull_request') {
                if ([string]$evidence.pullRequest -notmatch '^[1-9][0-9]*$' -or
                    [string]$evidence.commit -cnotmatch '^[0-9a-f]{40}$') { continue }
                $pr = & $Api "pulls/$($evidence.pullRequest)"
                $testedCommit = & $Api "git/commits/$($evidence.commit)"
            }
            if (Test-CiEvidence $evidence $Current $run $jobs $pr $testedCommit) {
                return @{ runId = $run.id; runAttempt = $run.run_attempt; url = $run.html_url }
            }
        }
    }
    catch { Write-Warning 'CI evidence could not be verified; running the full suite.' }
    return $null
}

Export-ModuleMember -Function Get-CiDependencyHash, Test-CiEvidence, Find-CiEvidence
