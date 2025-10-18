using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Asp.net.Services;

/// <summary>
/// Background service để monitor connection status và tự động test ping
/// </summary>
public class ConnectionMonitor : BackgroundService
{
    private readonly RegisterServices _registerServices;
    private readonly ILogger<ConnectionMonitor> _logger;
    private int _testCounter = 0;

    public ConnectionMonitor(
        RegisterServices registerServices,
        ILogger<ConnectionMonitor> logger)
    {
        _registerServices = registerServices;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔍 ConnectionMonitor started (Background tests DISABLED)");
        
        // Chỉ monitor, không gửi test tự động nữa
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(30000, stoppingToken); // Sleep forever
        }

        _logger.LogInformation("🛑 ConnectionMonitor stopped");
    }

    private async Task TestWebSocketPing(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📤 [WebSocket] Sending Ping...");
            await _registerServices.SendWebSocketPingAsync($"Background test #{_testCounter}", cancellationToken);
            _logger.LogInformation("✅ [WebSocket] Ping sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [WebSocket] Ping failed");
        }
    }

    private async Task TestTcpPing(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📤 [TCP] Sending Ping...");
            await _registerServices.SendTcpPingAsync($"Background test #{_testCounter}", cancellationToken);
            _logger.LogInformation("✅ [TCP] Ping sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [TCP] Ping failed");
        }
    }

    private async Task TestWebSocketMessage(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📤 [WebSocket] Sending Message...");
            await _registerServices.SendWebSocketMessageAsync($"Background message #{_testCounter}", cancellationToken);
            _logger.LogInformation("✅ [WebSocket] Message sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [WebSocket] Message failed");
        }
    }

    private async Task TestTcpMessage(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📤 [TCP] Sending Message...");
            await _registerServices.SendTcpMessageAsync($"Background message #{_testCounter}", cancellationToken);
            _logger.LogInformation("✅ [TCP] Message sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [TCP] Message failed");
        }
    }
}
