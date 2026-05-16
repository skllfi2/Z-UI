# Test-IPC.ps1 - Test script for Z-UI IPC Broker
# Run this script after starting Z-UI to verify IPC functionality

param(
    [string]$BrokerHost = "127.0.0.1",
    [int]$BrokerPort = 27015,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$Passed = 0
$Failed = 0

function Write-TestResult {
    param([string]$Name, [bool]$Success, [string]$Details = "")
    $icon = if ($Success) { "✓" } else { "✗" }
    $color = if ($Success) { "Green" } else { "Red" }
    Write-Host "[$icon] $Name" -ForegroundColor $color
    if ($Details) { Write-Host "    $Details" -ForegroundColor DarkGray }
    if ($Success) { $script:Passed++ } else { $script:Failed++ }
}

function Send-JsonRpc {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [string]$Method,
        [object]$Params = $null,
        [int]$Id = 1
    )
    
    $stream = $Client.GetStream()
    
    $request = @{
        jsonrpc = "2.0"
        method = $Method
        id = $Id
    }
    if ($Params) { $request.params = $Params }
    
    $json = $request | ConvertTo-Json -Compress
    $message = "Content-Length: $($json.Length)`r`n`r`n$json"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($message)
    
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    
    # Read response header
    $headerBytes = New-Object System.Collections.Generic.List[byte]
    while ($true) {
        $b = $stream.ReadByte()
        if ($b -lt 0) { throw "Connection closed" }
        $headerBytes.Add([byte]$b)
        if ($headerBytes.Count -ge 4) {
            $last4 = $headerBytes.ToArray()[-4..-1]
            if ($last4[0] -eq 13 -and $last4[1] -eq 10 -and $last4[2] -eq 13 -and $last4[3] -eq 10) {
                break
            }
        }
    }
    
    $header = [System.Text.Encoding]::UTF8.GetString($headerBytes.ToArray())
    if ($header -match "Content-Length: (\d+)") {
        $contentLength = [int]$Matches[1]
        
        # Read body
        $bodyBytes = New-Object byte[] $contentLength
        $stream.Read($bodyBytes, 0, $contentLength) | Out-Null
        $body = [System.Text.Encoding]::UTF8.GetString($bodyBytes)
        
        return $body | ConvertFrom-Json
    }
    
    throw "Invalid response header"
}

Write-Host "`n=== Z-UI IPC Broker Test Suite ===" -ForegroundColor Cyan
Write-Host "Target: ${BrokerHost}:${BrokerPort}`n"

# Test 1: TCP Connection
Write-Host "--- Test 1: TCP Connection ---" -ForegroundColor Yellow
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $connectTask = $client.ConnectAsync($BrokerHost, $BrokerPort)
    $connectTask.Wait(5000) | Out-Null
    Write-TestResult "TCP Connection" $client.Connected "Connected to port $BrokerPort"
} catch {
    Write-TestResult "TCP Connection" $false $_.Exception.Message
    Write-Host "`nCannot proceed without connection. Ensure Z-UI is running." -ForegroundColor Red
    exit 1
}

# Test 2: Client Handshake
Write-Host "`n--- Test 2: Client Handshake ---" -ForegroundColor Yellow
try {
    $response = Send-JsonRpc -Client $client -Method "client.hello" -Params @{
        clientId = "test-script"
        clientVersion = "1.0"
        protocolVersion = 1
        subscriptions = @("statusChanged", "profileChanged")
    } -Id 1
    
    $success = $response.error -eq $null -and $response.result.neighborId -ne $null
    $details = if ($success) { "Neighbor ID: $($response.result.neighborId)" } else { $response.error.message }
    Write-TestResult "Client Handshake" $success $details
} catch {
    Write-TestResult "Client Handshake" $false $_.Exception.Message
}

# Test 3: Get Status
Write-Host "`n--- Test 3: Get Status ---" -ForegroundColor Yellow
try {
    $response = Send-JsonRpc -Client $client -Method "zui.getStatus" -Id 2
    
    $success = $response.error -eq $null -and $response.result -ne $null
    if ($success) {
        $details = "Z-UI: $($response.result.zuiConnected), WinDivert: $($response.result.windivert)"
        if ($Verbose) {
            $details += ", Active profiles: $($response.result.activeProfiles -join ', ')"
        }
    } else {
        $details = $response.error.message
    }
    Write-TestResult "Get Status" $success $details
} catch {
    Write-TestResult "Get Status" $false $_.Exception.Message
}

# Test 4: Get Profiles
Write-Host "`n--- Test 4: Get Profiles ---" -ForegroundColor Yellow
try {
    $response = Send-JsonRpc -Client $client -Method "zui.getProfiles" -Id 3
    
    $success = $response.error -eq $null -and $response.result.profiles -ne $null
    if ($success) {
        $profiles = $response.result.profiles | ForEach-Object { $_.name }
        $details = "Profiles: $($profiles -join ', ')"
    } else {
        $details = $response.error.message
    }
    Write-TestResult "Get Profiles" $success $details
} catch {
    Write-TestResult "Get Profiles" $false $_.Exception.Message
}

# Test 5: Activate Profile (optional - requires Z-UI service running)
Write-Host "`n--- Test 5: Activate Profile ---" -ForegroundColor Yellow
try {
    $response = Send-JsonRpc -Client $client -Method "zui.activateProfile" -Params @{
        profile = "poe2-frankfurt"
    } -Id 4
    
    # This may fail if Z-UI service is not running, which is expected
    if ($response.error -and $response.error.code -eq -32000) {
        Write-TestResult "Activate Profile" $true "Expected: Service may not be running"
    } else {
        $success = $response.error -eq $null -and $response.result.ok -eq $true
        $details = if ($success) { "Profile activated" } else { $response.error.message }
        Write-TestResult "Activate Profile" $success $details
    }
} catch {
    Write-TestResult "Activate Profile" $false $_.Exception.Message
}

# Test 6: Invalid Method
Write-Host "`n--- Test 6: Invalid Method (Error Handling) ---" -ForegroundColor Yellow
try {
    $response = Send-JsonRpc -Client $client -Method "invalid.method" -Id 5
    
    $success = $response.error -ne $null -and $response.error.code -eq -32601
    $details = "Error code: $($response.error.code), Message: $($response.error.message)"
    Write-TestResult "Invalid Method Handling" $success $details
} catch {
    Write-TestResult "Invalid Method Handling" $false $_.Exception.Message
}

# Cleanup
$client.Close()

# Summary
Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $Passed" -ForegroundColor Green
Write-Host "Failed: $Failed" -ForegroundColor $(if ($Failed -gt 0) { "Red" } else { "Green" })

if ($Failed -eq 0) {
    Write-Host "`n✓ All tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n✗ Some tests failed. Check Z-UI logs for details." -ForegroundColor Red
    exit 1
}
