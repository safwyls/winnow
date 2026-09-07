[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$InstallerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-SilentProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "'$FilePath' exited with code $($process.ExitCode)."
    }
}

function Remove-VerifiedSmokeRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedTempRoot = (Resolve-Path -LiteralPath ([System.IO.Path]::GetTempPath())).Path.TrimEnd('\')
    $expectedPrefix = Join-Path $resolvedTempRoot 'Winnow-installer-smoke-'
    if (-not $resolvedPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPath -notmatch '^.+\\Winnow-installer-smoke-[0-9a-f]{32}$') {
        throw "Refusing to recursively remove a path outside this smoke test's temporary directory: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

if ($env:GITHUB_ACTIONS -cne 'true') {
    throw 'The installer smoke test runs only on a fresh GitHub Actions runner to avoid changing a local installer registration.'
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer does not exist: $InstallerPath"
}
$installerPath = (Resolve-Path -LiteralPath $InstallerPath).Path

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Winnow-installer-smoke-" + [guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $smokeRoot 'installed'
$dataDirectory = Join-Path $smokeRoot 'data'
$applicationPath = Join-Path $installDirectory 'Winnow.exe'
$uninstallerPath = Join-Path $installDirectory 'unins000.exe'
$databasePath = Join-Path $dataDirectory 'winnow.db'
$applicationProcess = $null

try {
    $null = New-Item -ItemType Directory -Path $smokeRoot -Force
    $installerArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-',
        ('/DIR="{0}"' -f $installDirectory)
    )

    Invoke-SilentProcess -FilePath $installerPath -Arguments $installerArguments
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "The silent install did not create $applicationPath."
    }

    $applicationArguments = '--no-sync --data-dir "{0}"' -f $dataDirectory
    $applicationProcess = Start-Process -FilePath $applicationPath -ArgumentList $applicationArguments -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (Test-Path -LiteralPath $databasePath -PathType Leaf) {
            break
        }
        if ($applicationProcess.HasExited) {
            throw "Winnow exited during the isolated startup check with code $($applicationProcess.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
        throw 'Winnow did not create winnow.db in the isolated --data-dir directory.'
    }
    Start-Sleep -Seconds 2
    if ($applicationProcess.HasExited) {
        throw "Winnow exited after database creation with code $($applicationProcess.ExitCode)."
    }
    Stop-Process -Id $applicationProcess.Id -Force
    if (-not $applicationProcess.WaitForExit(10000)) {
        throw 'Winnow did not stop after the isolated startup check.'
    }
    $applicationProcess = $null

    $sentinelPath = Join-Path $dataDirectory 'preserve-after-uninstall.txt'
    [System.IO.File]::WriteAllText($sentinelPath, 'keep this user data')

    Invoke-SilentProcess -FilePath $installerPath -Arguments $installerArguments
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw 'The silent reinstall did not preserve the selected install location.'
    }

    if (-not (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) {
        throw "The install did not create its uninstaller: $uninstallerPath"
    }
    Invoke-SilentProcess -FilePath $uninstallerPath -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')

    if (Test-Path -LiteralPath $applicationPath -PathType Leaf) {
        throw 'The silent uninstall left Winnow.exe in the install directory.'
    }
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'The uninstall removed user data outside the install directory.'
    }

    Write-Host "Windows installer smoke test passed: $installerPath"
}
finally {
    if ($null -ne $applicationProcess -and -not $applicationProcess.HasExited) {
        Stop-Process -Id $applicationProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-VerifiedSmokeRoot -Path $smokeRoot
}
