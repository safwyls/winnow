[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$target = [System.Management.Automation.SemanticVersion]::Parse($Version)
$headers = @{ 'User-Agent' = 'Winnow-portable-upgrade-smoke'; Accept = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }
$extension = if ($Runtime -eq 'win-x64') { 'zip' } else { 'tar.gz' }
$candidates = @()
for ($page = 1; $page -le 10; $page++) {
    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/safwyls/winnow/releases?per_page=100&page=$page" -Headers $headers
    foreach ($release in $releases) {
        if ($release.draft) { continue }
        try { $candidate = [System.Management.Automation.SemanticVersion]::Parse($release.tag_name.TrimStart('v')) }
        catch { continue }
        if ($candidate -ge $target) { continue }
        $name = "Winnow-$candidate-$Runtime.$extension"
        $assets = @($release.assets | Where-Object { $_.name -ceq $name })
        if ($assets.Count -eq 1) { $candidates += @{ Version = $candidate; Asset = $assets[0]; Tag = $release.tag_name } }
    }
    if ($releases.Count -lt 100) { break }
}
$previous = $candidates | Sort-Object -Property Version -Descending | Select-Object -First 1
if ($null -eq $previous) { throw "No published $Runtime portable archive earlier than $Version exists; refusing to substitute a same-version upgrade." }
$expectedUrl = "https://github.com/safwyls/winnow/releases/download/$($previous.Tag)/$($previous.Asset.name)"
if ($previous.Asset.browser_download_url -cne $expectedUrl -or $previous.Asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
    throw 'The previous portable archive has no valid official URL or GitHub SHA-256 digest.'
}
$expectedHash = $Matches[1]
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath)))
Invoke-WebRequest -Uri $expectedUrl -OutFile $OutputPath
if ((Get-Item -LiteralPath $OutputPath).Length -ne $previous.Asset.size -or
    (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash -ine $expectedHash) {
    throw 'Previous portable archive size or checksum does not match GitHub.'
}
@{ previousVersion = $previous.Version.ToString(); targetVersion = $Version; runtime = $Runtime; sha256 = $expectedHash } |
    ConvertTo-Json | Set-Content -LiteralPath "$OutputPath.evidence.json" -Encoding utf8NoBOM
Write-Host "Portable upgrade smoke baseline: $($previous.Tag) -> $Version ($Runtime)"
