param(
    [ValidateSet('Empty', 'Refill')]
    [string]$Action = 'Empty',
    [Guid]$DeviceId = '7e8c0d1a-6b39-47b5-928a-64f3f1039e21',
    [string]$BaseUrl = 'http://localhost:5200',
    [string]$DeviceKey = 'local-development-key',
    [decimal]$RefillGrams = 500
)

$ErrorActionPreference = 'Stop'
if ($RefillGrams -lt 50) { throw 'Use at least 50 g for a refill.' }
$status = Invoke-RestMethod "$BaseUrl/api/v1/devices/$DeviceId/status"
$observed = [DateTimeOffset]::UtcNow
if ($status.latestReading -and [DateTimeOffset]$status.latestReading.observedAt -ge $observed) {
    throw 'The latest reading is in the future. Wait until its timestamp passes.'
}
$sequence = $observed.ToUnixTimeMilliseconds()
if ($status.latestReading -and $status.latestReading.sequence -ge $sequence) {
    $sequence = [long]$status.latestReading.sequence + 1
}
$weight = if ($Action -eq 'Empty') { 0 } else { $RefillGrams }
$body = @{
    batchId = "notification-test-$sequence"
    readings = @(@{
        sequence = $sequence
        observedAt = $observed.ToString('O')
        weightGrams = $weight
        rawReading = $status.calibration.zero_offset + $weight * $status.calibration.calibration_factor
    })
} | ConvertTo-Json -Depth 4

$result = Invoke-RestMethod "$BaseUrl/api/v1/devices/$DeviceId/readings:batch" -Method Post `
    -ContentType 'application/json' -Headers @{ 'X-Device-Key' = $DeviceKey } -Body $body
$alerts = @(Invoke-RestMethod "$BaseUrl/api/v1/notifications")
[pscustomobject]@{
    action = $Action
    weightGrams = $weight
    insertedCount = $result.insertedCount
    activeAlerts = @($alerts | Where-Object { $_.deviceId -eq $DeviceId.ToString() -and -not $_.resolvedAt }).Count
}
