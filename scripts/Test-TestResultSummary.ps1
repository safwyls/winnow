$ErrorActionPreference = 'Stop'

$directory = Join-Path ([IO.Path]::GetTempPath()) ("winnow-trx-summary-test-" + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $directory 'summary.md'
[IO.Directory]::CreateDirectory($directory) | Out-Null

try {
    $trx = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Times start="2026-09-08T12:00:00.0000000Z" finish="2026-09-08T12:00:05.0000000Z" />
  <Results>
    <UnitTestResult testId="1" testName="Example.Tests.Fast" outcome="Passed" duration="00:00:00.2500000" />
    <UnitTestResult testId="2" testName="Example.Tests.Slow|Named" outcome="Failed" duration="00:00:02.5000000" />
  </Results>
  <TestDefinitions>
    <UnitTest id="1"><TestMethod codeBase="D:\a\Example.Tests.dll" className="Example.Tests" name="Fast" /></UnitTest>
    <UnitTest id="2"><TestMethod codeBase="D:\a\Example.Tests.dll" className="Example.Tests" name="Slow" /></UnitTest>
  </TestDefinitions>
</TestRun>
'@
    [IO.File]::WriteAllText((Join-Path $directory 'example.trx'), $trx)

    & (Join-Path $PSScriptRoot 'Summarize-TestResults.ps1') `
        -ResultsDirectory $directory -OutputPath $output -SlowTestCount 2 | Out-Null

    $summary = [IO.File]::ReadAllText($output)
    if ($summary -notmatch '\| Example.Tests.dll \| 2 \| 1 \| 1 \| 0 \| 5.00 s \| 2.75 s \|') {
        throw "The assembly timing row was not summarized correctly:`n$summary"
    }
    if ($summary -notmatch 'Example.Tests.Slow\\\|Named \| Failed \| 2.50 s') {
        throw "The slow-test row was not ordered or escaped correctly:`n$summary"
    }

    Write-Output 'Test-result summary checks passed.'
}
finally {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}
