# Read installed release evidence without loading its managed code or retaining binary locks.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Export-InstalledUpdateHelper([string]$Installation, [string]$Destination) {
    $legacy = Join-Path $Installation 'Winnow.dll'
    $assembly = if (Test-Path -LiteralPath $legacy -PathType Leaf) { $legacy } else {
        Join-Path $Installation 'update-helper/Winnow.Update.Helper.dll'
    }
    Add-Type -AssemblyName System.Reflection.Metadata
    $stream = [IO.File]::OpenRead($assembly)
    try {
        $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
            $found = @($metadata.ManifestResources | Where-Object {
                $metadata.GetString($metadata.GetManifestResource($_).Name) -ceq 'Winnow.UpdateHelper.ps1'
            })
            if ($found.Count -ne 1) { throw 'The installed release must contain exactly one embedded Windows update helper.' }
            $resource = $metadata.GetManifestResource($found[0])
            $directory = $reader.PEHeaders.CorHeader.ResourcesDirectory
            if (-not $resource.Implementation.IsNil -or $resource.Offset -gt ($directory.Size - 4)) {
                throw 'The installed Windows update helper is not a bounded embedded resource.'
            }
            $blob = $reader.GetSectionData($directory.RelativeVirtualAddress).GetReader([int]$resource.Offset, $directory.Size - [int]$resource.Offset)
            $length = $blob.ReadInt32()
            if ($length -le 0 -or $length -gt 1MB -or $length -gt $blob.RemainingBytes) {
                throw 'The installed Windows update helper resource has an invalid length.'
            }
            [IO.File]::WriteAllBytes($Destination, $blob.ReadBytes($length))
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
    $manifest = Get-Content -LiteralPath (Join-Path $Installation 'release-info.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{
        version = $manifest.version
        assembly = [IO.Path]::GetRelativePath($Installation, $assembly)
        assemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
        resource = 'Winnow.UpdateHelper.ps1'
        helperSha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Read-InstalledLibraryEvidence([string]$Database) {
    $code = @'
import sqlite3, sys, json
from pathlib import Path
with sqlite3.connect(Path(sys.argv[1]).resolve().as_uri() + '?mode=ro', uri=True) as c:
    assert c.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    evidence = {
        'works': c.execute('SELECT id,name FROM works ORDER BY id').fetchall(),
        'releases': c.execute('SELECT id,work_id,name FROM releases ORDER BY id').fetchall(),
        'ownerships': c.execute('SELECT id,release_id,store,account_ref,acquired_at,license_type,price_paid_cents FROM ownerships ORDER BY id').fetchall()
    }
    assert all(evidence.values()), 'The sample library must include works, releases and ownerships'
    print(json.dumps(evidence, sort_keys=True))
'@
    $result = & python -c $code $Database
    if ($LASTEXITCODE -ne 0 -or -not $result) { throw 'Installed sample library integrity or identity check failed.' }
    return $result
}

function Initialize-InstalledSmokeLibrary([string]$Database) {
    # Published Release builds omit --seed-sample. Seed only the isolated database
    # after the old frontend and backend have stopped, using schema present in that release.
    $code = @'
import sqlite3, sys
from pathlib import Path
with sqlite3.connect(Path(sys.argv[1]).resolve().as_uri() + '?mode=rw', uri=True) as c:
    c.execute('PRAGMA foreign_keys=ON')
    c.execute('INSERT INTO works(id,name) VALUES (?,?)', (-159,'Installed upgrade fixture'))
    c.execute('INSERT INTO releases(id,work_id,name) VALUES (?,?,?)', (-159,-159,'Installed upgrade edition'))
    c.execute('INSERT INTO ownerships(id,release_id,store,account_ref,acquired_at,license_type,price_paid_cents) VALUES (?,?,?,?,?,?,?)', (-159,-159,'steam','11111','2020-01-02 00:00:00','retail',500))
'@
    & python -c $code $Database
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the isolated previous-release library.' }
}
