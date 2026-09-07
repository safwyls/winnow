[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$') {
    throw 'Use X.Y.Z or X.Y.Z-prerelease, without a v prefix or build metadata.'
}
$parts = $Version.Split('-', 2)
foreach ($part in $parts[0].Split('.')) {
    if ([long]$part -gt 65535) { throw 'Version components must fit a Windows file version (0-65535).' }
}
if ($parts.Count -eq 2) {
    foreach ($identifier in $parts[1].Split('.')) {
        if ($identifier -match '^0[0-9]+$') { throw 'Numeric prerelease identifiers cannot have leading zeroes.' }
    }
}
[pscustomobject]@{
    Version = $Version
    Numeric = $parts[0]
    Prerelease = ($parts.Count -eq 2)
}
