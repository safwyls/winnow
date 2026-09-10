[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$target = [System.Management.Automation.SemanticVersion]::Parse($Version)
$headers = @{ 'User-Agent' = 'Winnow-upgrade-smoke'; Accept = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }
$candidates = @()
for ($page = 1; $page -le 10; $page++) {
    $releases = @(Invoke-RestMethod -Uri "https://api.github.com/repos/safwyls/winnow/releases?per_page=100&page=$page" -Headers $headers)
    foreach ($release in $releases) {
        if ($release.draft) { continue }
        try { $candidate = [System.Management.Automation.SemanticVersion]::Parse($release.tag_name.TrimStart('v')) }
        catch { continue }
        if ($candidate -ge $target) { continue }
        $name = "Winnow-$candidate-win-x64-setup.exe"
        $assets = @($release.assets | Where-Object { $_.name -ceq $name })
        if ($assets.Count -eq 1) { $candidates += @{ Version = $candidate; Asset = $assets[0]; Tag = $release.tag_name } }
    }
    if ($releases.Count -lt 100) { break }
}
$previous = $candidates | Sort-Object -Property Version -Descending | Select-Object -First 1
if ($null -eq $previous) { throw "No published Windows installer earlier than $Version exists; an older-release upgrade smoke test cannot run." }
$url = [Uri]$previous.Asset.browser_download_url
$expectedUrl = "https://github.com/safwyls/winnow/releases/download/$($previous.Tag)/$($previous.Asset.name)"
if ($url.AbsoluteUri -cne $expectedUrl -or $previous.Asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
    throw 'The previous installer has no valid official URL or GitHub SHA-256 digest.'
}
$expectedHash = $Matches[1]
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath)))
Invoke-WebRequest -Uri $url -OutFile $OutputPath
if ((Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash -ine $expectedHash) { throw 'Previous installer checksum does not match GitHub.' }
Write-Host "Upgrade smoke baseline: $($previous.Tag) -> $Version"
