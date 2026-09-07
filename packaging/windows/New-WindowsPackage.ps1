[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RequiredDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Name does not exist or is not a directory: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-ReleaseManifest {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestJson,

        [Parameter(Mandatory)]
        [string]$ExpectedVersion
    )

    try {
        $manifest = $ManifestJson | ConvertFrom-Json
    }
    catch {
        throw "release-info.json is not valid JSON: $($_.Exception.Message)"
    }

    foreach ($propertyName in @('version', 'runtime', 'commit')) {
        if ($null -eq $manifest.PSObject.Properties[$propertyName]) {
            throw "release-info.json is missing '$propertyName'."
        }
    }

    if ($manifest.version -cne $ExpectedVersion) {
        throw "release-info.json version '$($manifest.version)' does not match '$ExpectedVersion'."
    }
    if ($manifest.runtime -cne 'win-x64') {
        throw "release-info.json runtime must be 'win-x64', not '$($manifest.runtime)'."
    }
    if ([string]$manifest.commit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'release-info.json commit must be a 40-character lowercase SHA-1.'
    }
}

function Assert-PortableArchive {
    param(
        [Parameter(Mandatory)]
        [string]$ArchivePath,

        [Parameter(Mandatory)]
        [string]$ExpectedVersion
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($null -eq $archive.GetEntry('Winnow.exe')) {
            throw 'Portable archive does not contain Winnow.exe.'
        }
        $manifestEntry = $archive.GetEntry('release-info.json')
        if ($null -eq $manifestEntry) {
            throw 'Portable archive does not contain release-info.json.'
        }

        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try {
            Assert-ReleaseManifest -ManifestJson $reader.ReadToEnd() -ExpectedVersion $ExpectedVersion
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$release = & (Join-Path $repositoryRoot 'packaging\Resolve-Version.ps1') -Version $Version
$Version = $release.Version
$publishDirectory = Resolve-RequiredDirectory -Path $PublishDirectory -Name 'PublishDirectory'
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'Winnow.exe') -PathType Leaf)) {
    throw "PublishDirectory does not contain Winnow.exe: $publishDirectory"
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'appsettings.local.json') -PathType Leaf) {
    throw 'PublishDirectory contains appsettings.local.json, which must not enter a release artifact.'
}
$manifestPath = Join-Path $publishDirectory 'release-info.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "PublishDirectory does not contain release-info.json: $publishDirectory"
}
Assert-ReleaseManifest -ManifestJson (Get-Content -LiteralPath $manifestPath -Raw) -ExpectedVersion $Version

if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
}
$outputDirectory = Resolve-RequiredDirectory -Path $OutputDirectory -Name 'OutputDirectory'

$installerSource = Join-Path $PSScriptRoot 'Winnow.iss'
$iconFile = Join-Path $repositoryRoot 'src\Winnow.App\Assets\Icons\dragon.ico'
if (-not (Test-Path -LiteralPath $iconFile -PathType Leaf)) {
    throw "The installer icon is missing: $iconFile"
}

$innoCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $innoCompiler -PathType Leaf)) {
    throw "Inno Setup 6 is required at '$innoCompiler'. GitHub's windows-2025 runner provides this compiler."
}

$assetStem = "Winnow-$Version-win-x64"
$installerPath = Join-Path $outputDirectory "$assetStem-setup.exe"
$portableArchivePath = Join-Path $outputDirectory "$assetStem.zip"
foreach ($artifact in @($installerPath, $portableArchivePath)) {
    if (Test-Path -LiteralPath $artifact -PathType Leaf) {
        Remove-Item -LiteralPath $artifact -Force
    }
}

& $innoCompiler "/DSourceDir=$publishDirectory" "/DOutputDir=$outputDirectory" "/DAppVersion=$Version" "/DNumericVersion=$($release.Numeric)" "/DIconFile=$iconFile" $installerSource
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDirectory,
    $portableArchivePath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)
Assert-PortableArchive -ArchivePath $portableArchivePath -ExpectedVersion $Version

foreach ($artifact in @($installerPath, $portableArchivePath)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Packaging did not produce the expected artifact: $artifact"
    }
}

[pscustomobject]@{
    Installer = $installerPath
    PortableArchive = $portableArchivePath
    Version = $Version
}
