param(
    [Parameter(Mandatory)][string] $Revision,
    [Parameter(Mandatory)][string] $OutputFile
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
# Use PowerShell's matching Roslyn assemblies instead of binding to another SDK's version.
if (-not ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
    Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
    Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
}
$resolvedRevision = & git -C $repository rev-parse --verify "$Revision^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'The source revision must identify a commit.' }
$files = & git -C $repository ls-tree -r --name-only $resolvedRevision -- tests/Winnow.Tests tests/Winnow.Ui.Tests
if ($LASTEXITCODE -ne 0) { throw 'The source tree could not be enumerated.' }
$contracts = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    if (-not $file.EndsWith('.cs')) { continue }
    $source = (& git -C $repository show "${resolvedRevision}:$file") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "The source file could not be read: $file" }
    # The embedded sign-in host and its pure capture policies also move with the UI.
    if (-not $file.StartsWith('tests/Winnow.Ui.Tests/') -and $source -notmatch 'Winnow\.(App|Auth\.WebView|Core\.Auth|Presentation|Covers\.Avalonia)|using Avalonia') { continue }
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source)
    foreach ($method in $tree.GetRoot().DescendantNodes()) {
        if ($method -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax]) { continue }
        $attributes = @($method.AttributeLists | ForEach-Object { $_.Attributes } | ForEach-Object { $_.Name.ToString() })
        if (-not @($attributes | Where-Object { $_ -match '(^|\.)(Avalonia|Skippable)?(Fact|Theory)(Attribute)?$' }).Count) { continue }
        $class = $method.Ancestors() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] } | Select-Object -First 1
        $contracts.Add([pscustomobject]@{ id = $class.Identifier.ValueText + '.' + $method.Identifier.ValueText; source = $file })
    }
}
if (@($contracts | Group-Object id | Where-Object Count -gt 1).Count) { throw 'Duplicate test names require a qualified class identity.' }
[ordered]@{ sourceRevision = $resolvedRevision; tests = $contracts } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputFile -Encoding utf8
Write-Output "Collected $($contracts.Count) test methods from $resolvedRevision."
