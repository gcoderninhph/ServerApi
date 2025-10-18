# Stop all running instances
Write-Host "🛑 Stopping all running instances..." -ForegroundColor Yellow
Get-Process -Name "Client.Asp.net" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "Asp.net" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# Build
Write-Host "`n🔨 Building Client.Asp.net..." -ForegroundColor Cyan
cd "$PSScriptRoot\Client.Asp.net"
dotnet build --no-restore

Write-Host "`n🔨 Building Asp.net..." -ForegroundColor Cyan
cd "$PSScriptRoot\Asp.net"
dotnet build --no-restore

# Start Server
Write-Host "`n🚀 Starting Server (Asp.net)..." -ForegroundColor Green
$serverJob = Start-Job -ScriptBlock {
    cd "D:\Code\CSharp\ServerApi\Asp.net"
    dotnet run --no-build
}
Start-Sleep -Seconds 5

# Start Client
Write-Host "🚀 Starting Client (Client.Asp.net)..." -ForegroundColor Green
$clientJob = Start-Job -ScriptBlock {
    cd "D:\Code\CSharp\ServerApi\Client.Asp.net"
    dotnet run --no-build --project Client.Asp.net.csproj
}

Write-Host "`n✅ Both servers started!" -ForegroundColor Green
Write-Host "📊 Monitor logs:" -ForegroundColor Cyan
Write-Host "  - Server: Receive-Job $($serverJob.Id)" -ForegroundColor Gray
Write-Host "  - Client: Receive-Job $($clientJob.Id)" -ForegroundColor Gray
Write-Host "`nPress Ctrl+C to view logs..." -ForegroundColor Yellow

# Keep running and show logs
while ($true) {
    Start-Sleep -Seconds 5
    Write-Host "`n=== Server Output ===" -ForegroundColor Magenta
    Receive-Job $serverJob -Keep | Select-Object -Last 10
    Write-Host "`n=== Client Output ===" -ForegroundColor Magenta
    Receive-Job $clientJob -Keep | Select-Object -Last 10
}
