# Colors for output
$RED = 'Red'
$GREEN = 'Green'
$YELLOW = 'Yellow'
$BLUE = 'Blue'
$NC = 'White'

$API_URL = if ($env:API_URL) { $env:API_URL } else { "http://localhost:5000" }

Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "EasyDispatch OpenTelemetry Demo - Test Suite" -ForegroundColor $BLUE
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host ""

# Check if API is running
Write-Host "Checking if API is running..." -ForegroundColor $YELLOW
try {
    $response = Invoke-WebRequest -Uri "${API_URL}/health" -UseBasicParsing -ErrorAction Stop
    Write-Host "API is running" -ForegroundColor $GREEN
} catch {
    Write-Host "API is not running at ${API_URL}" -ForegroundColor $RED
    Write-Host "Start the API with: dotnet run" -ForegroundColor $YELLOW
    exit 1
}
Write-Host ""

# Test 1: Get User (Query)
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "1 Test: Get User (Query)" -ForegroundColor $YELLOW
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Expected Activity: EasyDispatch.Query.GetUserQuery" -ForegroundColor $GREEN
Write-Host ""
$response = Invoke-WebRequest -Uri "${API_URL}/users/5" -UseBasicParsing
($response.Content | ConvertFrom-Json) | ConvertTo-Json
Write-Host ""
Start-Sleep -Seconds 1

# Test 2: Create User (Command + Notifications)
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "2 Test: Create User (Command + Notifications)" -ForegroundColor $YELLOW
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Expected Activities:"
Write-Host "  - EasyDispatch.Command.CreateUserCommand" -ForegroundColor $GREEN
Write-Host "  - EasyDispatch.Notification.UserCreatedNotification" -ForegroundColor $GREEN
Write-Host "    3 handlers (Email, Analytics, Cache)"
Write-Host ""
$body = @{
    name = "John Doe"
    email = "john@example.com"
} | ConvertTo-Json
$response = Invoke-WebRequest -Uri "${API_URL}/users" -Method POST -Body $body -ContentType "application/json" -UseBasicParsing
($response.Content | ConvertFrom-Json) | ConvertTo-Json
Write-Host ""
Start-Sleep -Seconds 1

# Test 3: Get User Orders - Success
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "3 Test: Get User Orders - Success" -ForegroundColor $YELLOW
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Expected Activity: EasyDispatch.Query.GetUserOrdersQuery" -ForegroundColor $GREEN
Write-Host "Status: Ok" -ForegroundColor $GREEN
Write-Host ""
$response = Invoke-WebRequest -Uri "${API_URL}/users/5/orders" -UseBasicParsing
($response.Content | ConvertFrom-Json) | ConvertTo-Json
Write-Host ""
Start-Sleep -Seconds 1

# Test 4: Get User Orders - Error
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "4 Test: Get User Orders - Error (Demo)" -ForegroundColor $YELLOW
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Expected Activity: EasyDispatch.Query.GetUserOrdersQuery" -ForegroundColor $GREEN
Write-Host "Status: Error" -ForegroundColor $RED
Write-Host "Exception: User 15 not found"
Write-Host ""
try {
    $response = Invoke-WebRequest -Uri "${API_URL}/users/15/orders" -UseBasicParsing -ErrorAction Stop
    Write-Host $response.Content
} catch {
    Write-Host $_.Exception.Message
}
Write-Host ""
Start-Sleep -Seconds 1

# Test 5: Delete User (Void Command)
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "5 Test: Delete User (Void Command)" -ForegroundColor $YELLOW
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Expected Activity: EasyDispatch.Command.DeleteUserCommand" -ForegroundColor $GREEN
Write-Host ""
try {
    $response = Invoke-WebRequest -Uri "${API_URL}/users/999" -Method DELETE -UseBasicParsing -ErrorAction Stop
    Write-Host "HTTP Status: $($response.StatusCode)"
} catch {
    Write-Host "HTTP Status: $($_.Exception.Response.StatusCode.Value__)"
}
Write-Host ""
Start-Sleep -Seconds 1

# Test 6: Multiple Rapid Requests (Load Test)
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "6 Test: Multiple Rapid Requests" -ForegroundColor $YELLOW
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Sending 10 rapid requests to test tracing under load..."
Write-Host ""
$jobs = @()
for ($i = 1; $i -le 10; $i++) {
    $jobs += Start-Job -ScriptBlock {
        param($url)
        Invoke-WebRequest -Uri $url -UseBasicParsing | Out-Null
    } -ArgumentList "${API_URL}/users/$i"
}
$jobs | Wait-Job | Remove-Job
Write-Host "Completed 10 concurrent requests" -ForegroundColor $GREEN
Write-Host ""

# Summary
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host "Test Suite Complete!" -ForegroundColor $GREEN
Write-Host "============================================" -ForegroundColor $BLUE
Write-Host ""
Write-Host "Check the traces in:" -ForegroundColor $YELLOW
Write-Host "  Console output (if using Console exporter)"
Write-Host "  Jaeger UI at http://localhost:16686 (if using Jaeger)"
Write-Host ""
Write-Host "What to look for:" -ForegroundColor $YELLOW
Write-Host "  Activity names: EasyDispatch.Query/Command/Notification.*"
Write-Host "  Parent-child relationships (HTTP -> Mediator)"
Write-Host "  Tags: operation, message_type, handler_type, etc."
Write-Host "  Error status on failed requests"
Write-Host "  Multiple handlers for notifications"
Write-Host ""