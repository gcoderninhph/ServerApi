using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Client.Asp.net.Services;

/// <summary>
/// Hosted service để tự động kết nối ClientCore với server khi startup
/// </summary>
public class AutoConnectHostedService : IHostedService
{
    private readonly RegisterServices _registerServices;
    private readonly ILogger<AutoConnectHostedService> _logger;

    public AutoConnectHostedService(
        RegisterServices registerServices,
        ILogger<AutoConnectHostedService> logger)
    {
        _registerServices = registerServices;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Starting AutoConnect...");

        try
        {
            // Tự động kết nối WebSocket Core với Asp.net server
            await _registerServices.ConnectWebSocketAsync("ws://localhost:5000/ws");
            _logger.LogInformation("✅ Auto-connected WebSocket Core to ws://localhost:5000/ws");

            // Tự động kết nối TCP Core với Asp.net server
            await _registerServices.ConnectTcpAsync("localhost", 5003);
            _logger.LogInformation("✅ Auto-connected TCP Core to localhost:5003");

            _logger.LogInformation("🎉 ClientCore is ready to send messages!");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Auto-connect failed. Please connect manually via API.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 Stopping AutoConnect...");

        try
        {
            await _registerServices.DisconnectWebSocketAsync();
            await _registerServices.DisconnectTcpAsync();
            _logger.LogInformation("✅ Disconnected from server");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during disconnect");
        }
    }
}
