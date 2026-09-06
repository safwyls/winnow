param(
    [Parameter(Mandatory = $true)][string]$AppPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
$dataDirectory = Join-Path ([IO.Path]::GetTempPath()) ('winnow-accessibility-' + [Guid]::NewGuid().ToString('N'))
$app = Start-Process -FilePath (Resolve-Path -LiteralPath $AppPath).Path -ArgumentList @(
    '--seed-sample', '--no-sync', '--data-dir', ('"' + $dataDirectory + '"')
) -WindowStyle Hidden -PassThru
$results = [Collections.Generic.List[object]]::new()

try {
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
    $window = $null
    for ($attempt = 0; $attempt -lt 300 -and !$window; $attempt++) {
        Start-Sleep -Milliseconds 100
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $processCondition)
    }
    if (!$window) { throw 'The isolated sample window did not appear.' }

    function Controls {
        $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.IsControlElement -and !$_.Current.IsOffscreen }
    }

    function Invoke-Button([string]$Name) {
        $button = Controls | Where-Object {
            $_.Current.Name -eq $Name -and
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button
        } | Select-Object -First 1
        if (!$button) { throw "No visible button named '$Name'." }
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 250
    }

    function Audit([string]$Surface, [string]$Region = '') {
        $controls = @(Controls)
        $interactive = @($controls | Where-Object { $_.Current.IsKeyboardFocusable })
        $bad = @($interactive | Where-Object {
            [string]::IsNullOrWhiteSpace($_.Current.Name) -or $_.Current.Name -match '^(Avalonia|Winnow)\.'
        })
        if ($bad.Count) { throw "$Surface has $($bad.Count) unnamed or implementation-named focusable controls." }
        if ($Region -and !($controls | Where-Object {
            $_.Current.Name -eq $Region -and $_.Current.ControlType -in @(
                [System.Windows.Automation.ControlType]::Group,
                [System.Windows.Automation.ControlType]::Window,
                [System.Windows.Automation.ControlType]::List)
        })) { throw "$Surface is missing region '$Region'." }
        $results.Add([PSCustomObject]@{ Surface = $Surface; NamedFocusableControls = $interactive.Count; Region = $Region })
    }

    Audit 'Feed' 'Feed'
    $allGames = (Controls | Where-Object { $_.Current.Name -match '^All games, \d+ games$' } | Select-Object -First 1).Current.Name
    Invoke-Button $allGames
    Audit 'Library grid'
    Invoke-Button 'List'
    Audit 'Library list' 'Games'
    $rows = @(Controls | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem })
    if (!$rows.Count -or @($rows | Where-Object { !$_.Current.Name }).Count) { throw 'Game list items must be named.' }
    $rows[0].SetFocus()
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne $rows[0].Current.Name) {
        throw 'Focus did not reach the named game list item.'
    }
    Invoke-Button 'Filters'
    Audit 'Filters' 'Filters'
    Invoke-Button 'Settings'
    Audit 'Platforms' 'Platforms'
    Invoke-Button 'LIBRARY'
    Audit 'Library settings' 'Library settings'
    $export = Controls | Where-Object { $_.Current.Name -eq 'Export acquisition CSV' } | Select-Object -First 1
    if (!$export) { throw 'Acquisition export button is missing.' }
    $export.SetFocus()
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne 'Export acquisition CSV') {
        throw 'Focus did not reach acquisition export.'
    }
    Invoke-Button 'APPEARANCE'
    Audit 'Appearance' 'Appearance'
    Invoke-Button 'MERGES'
    Audit 'Merges' 'Merges'
    Invoke-Button 'STATS'
    Audit 'Account stats' 'Account stats'
    Invoke-Button 'FEED'
    Invoke-Button "What you've told the feed"
    Audit 'Feed history' 'Feed'
    Invoke-Button $allGames
    Invoke-Button 'Grid'
    Invoke-Button 'Details'
    Audit 'Game details'
    $details = Controls | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -like 'Details of *'
    } | Select-Object -First 1
    if (!$details) { throw 'Game details did not expose its dialog name and role.' }
    Invoke-Button 'Close this game'
    Audit 'Returned to library'
    $results | Format-Table -AutoSize
    "Sample data: $dataDirectory"
}
finally {
    if (!$app.HasExited) { Stop-Process -Id $app.Id }
}
