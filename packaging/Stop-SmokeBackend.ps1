# Smoke callers supply an isolated data directory; never discover the user's backend.
function Stop-SmokeBackend([string]$DataDirectory) {
    $discoveryPath = Join-Path $DataDirectory 'backend/endpoint.json'
    if (-not (Test-Path -LiteralPath $discoveryPath)) { return }
    $discovery = Get-Content -LiteralPath $discoveryPath -Raw | ConvertFrom-Json
    $address = [uri]$discovery.address
    if ($address.Scheme -ne 'http' -or $address.Host -ne '127.0.0.1') {
        throw 'Smoke backend discovery is not a numeric loopback endpoint.'
    }
    $backend = Get-Process -Id $discovery.processId -ErrorAction SilentlyContinue
    if ($null -eq $backend) { return }
    $null = Invoke-RestMethod -Method Post -Uri ($address.AbsoluteUri.TrimEnd('/') + '/api/v1/lifecycle/shutdown') -Headers @{ Authorization = 'Bearer ' + $discovery.token } -TimeoutSec 10
    if (-not $backend.WaitForExit(30000)) { throw 'The smoke backend did not finish shutting down.' }
}
