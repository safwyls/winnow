[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('windows', 'linux')][string] $Platform)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CiEvidence.psm1') -Force
$root = Split-Path $PSScriptRoot -Parent
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve checked out commit.' }
$tree = & git -C $root rev-parse 'HEAD^{tree}'
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve checked out tree.' }
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve SDK version.' }
$event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json -AsHashtable
$pr = $event.pull_request
$current = @{
    schema = 1; kind = 'full'; repository = $env:GITHUB_REPOSITORY
    commit = $commit; tree = $tree; platform = $Platform; sdk = $sdk
    imageOS = $env:ImageOS; imageVersion = $env:ImageVersion
    dependencies = Get-CiDependencyHash $root
    runId = $env:GITHUB_RUN_ID; runAttempt = [int]$env:GITHUB_RUN_ATTEMPT
    event = $env:GITHUB_EVENT_NAME; pullRequest = $pr.number
    headCommit = $pr.head.sha; baseCommit = $pr.base.sha
    forceFull = $env:CI_FORCE_FULL -eq 'true'
}
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$apiRoot = "$env:GITHUB_API_URL/repos/$env:GITHUB_REPOSITORY"
$api = { param($path) Invoke-RestMethod -Uri "$apiRoot/$path" -Headers $headers -TimeoutSec 20 }
$readArtifact = {
    param($id)
    if ([string]$id -notmatch '^[1-9][0-9]*$') { throw 'Invalid artifact ID.' }
    $zipPath = [IO.Path]::GetTempFileName()
    try {
        Invoke-WebRequest -Uri "$apiRoot/actions/artifacts/$id/zip" -Headers $headers -OutFile $zipPath -TimeoutSec 30
        $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            if ($zip.Entries.Count -ne 1 -or $zip.Entries[0].FullName -cne "$Platform.json" -or
                $zip.Entries[0].Length -gt 16384) { throw 'Invalid evidence archive.' }
            $reader = [IO.StreamReader]::new($zip.Entries[0].Open())
            try { return $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable }
            finally { $reader.Dispose() }
        }
        finally { $zip.Dispose() }
    }
    finally { Remove-Item -LiteralPath $zipPath -Force }
}
$match = Find-CiEvidence $current $api $readArtifact
$reused = if ($match) { 'true' } else { 'false' }
"reused=$reused" >> $env:GITHUB_OUTPUT
if ($match) {
    $message = "Reusing full $Platform validation from [$($match.runId), attempt $($match.runAttempt)]($($match.url)). Tree, SDK, runner image and restored dependencies match; fresh audit and migration gates still apply."
}
else {
    $message = "No matching recent $Platform evidence: running full validation."
    New-Item -ItemType Directory -Path (Join-Path $root 'ci-evidence') -Force | Out-Null
    $current | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root "ci-evidence/$Platform.json") -Encoding utf8
}
Write-Host $message
$message >> $env:GITHUB_STEP_SUMMARY
