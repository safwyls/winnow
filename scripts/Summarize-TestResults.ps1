[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsDirectory,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [switch] $Append,

    [ValidateRange(1, 100)]
    [int] $SlowTestCount = 20
)

$ErrorActionPreference = 'Stop'

function Seconds([string] $duration) {
    if ([string]::IsNullOrWhiteSpace($duration)) { return 0.0 }
    return [TimeSpan]::Parse($duration, [Globalization.CultureInfo]::InvariantCulture).TotalSeconds
}

function DisplaySeconds([double] $seconds) {
    if ($seconds -ge 3600) {
        return [TimeSpan]::FromSeconds($seconds).ToString('h\:mm\:ss\.ff', [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($seconds -ge 60) {
        return [TimeSpan]::FromSeconds($seconds).ToString('m\:ss\.ff', [Globalization.CultureInfo]::InvariantCulture)
    }
    return $seconds.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + ' s'
}

function Markdown([string] $value) {
    return $value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

$files = if (Test-Path -LiteralPath $ResultsDirectory) {
    @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter '*.trx')
}
else {
    @()
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('## Test timing')
$lines.Add('')

if ($files.Count -eq 0) {
    $lines.Add('_No TRX results were produced._')
}
else {
    $runs = foreach ($file in $files) {
        $trx = [xml][IO.File]::ReadAllText($file.FullName)
        $results = @($trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']") | ForEach-Object { $_ })
        $definition = $trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='TestDefinitions']/*[local-name()='UnitTest']") |
            Select-Object -First 1
        $codeBase = [string]$definition.TestMethod.codeBase
        $assembly = if ($codeBase) { Split-Path $codeBase -Leaf } else { $file.BaseName }
        $times = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='Times']")
        $start = [DateTimeOffset]::Parse([string]$times.start)
        $finish = [DateTimeOffset]::Parse([string]$times.finish)

        [pscustomobject]@{
            Assembly = $assembly
            Count = $results.Count
            Passed = @($results | Where-Object outcome -eq 'Passed').Count
            Failed = @($results | Where-Object outcome -eq 'Failed').Count
            Skipped = @($results | Where-Object outcome -eq 'NotExecuted').Count
            WallSeconds = ($finish - $start).TotalSeconds
            TestSeconds = ($results | ForEach-Object { Seconds ([string]$_.duration) } | Measure-Object -Sum).Sum
            Results = $results
        }
    }

    $assemblies = $runs | Group-Object Assembly | ForEach-Object {
        [pscustomobject]@{
            Assembly = $_.Name
            Count = ($_.Group | Measure-Object -Property Count -Sum).Sum
            Passed = ($_.Group | Measure-Object -Property Passed -Sum).Sum
            Failed = ($_.Group | Measure-Object -Property Failed -Sum).Sum
            Skipped = ($_.Group | Measure-Object -Property Skipped -Sum).Sum
            WallSeconds = ($_.Group | Measure-Object -Property WallSeconds -Sum).Sum
            TestSeconds = ($_.Group | Measure-Object -Property TestSeconds -Sum).Sum
        }
    } | Sort-Object WallSeconds -Descending

    $lines.Add('| Assembly | Cases | Passed | Failed | Skipped | Wall time | Cumulative test time |')
    $lines.Add('|---|---:|---:|---:|---:|---:|---:|')
    foreach ($assembly in $assemblies) {
        $lines.Add('| ' + (Markdown $assembly.Assembly) + ' | ' + $assembly.Count + ' | ' +
            $assembly.Passed + ' | ' + $assembly.Failed + ' | ' + $assembly.Skipped + ' | ' +
            (DisplaySeconds $assembly.WallSeconds) + ' | ' + (DisplaySeconds $assembly.TestSeconds) + ' |')
    }

    $slow = @($runs | ForEach-Object { $_.Results } | ForEach-Object {
        [pscustomobject]@{
            Test = [string]$_.testName
            Outcome = [string]$_.outcome
            Duration = Seconds ([string]$_.duration)
        }
    } | Sort-Object Duration -Descending | Select-Object -First $SlowTestCount)

    $lines.Add('')
    $lines.Add("### Slowest $($slow.Count) cases")
    $lines.Add('')
    $lines.Add('| Test | Outcome | Duration |')
    $lines.Add('|---|---|---:|')
    foreach ($test in $slow) {
        $lines.Add('| ' + (Markdown $test.Test) + ' | ' + (Markdown $test.Outcome) + ' | ' +
            (DisplaySeconds $test.Duration) + ' |')
    }
}

$text = ($lines -join [Environment]::NewLine) + [Environment]::NewLine
$parent = Split-Path -Parent $OutputPath
if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }

if ($Append) {
    [IO.File]::AppendAllText($OutputPath, $text)
}
else {
    [IO.File]::WriteAllText($OutputPath, $text)
}

$text
