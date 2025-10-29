using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using kcp2k;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerApi.Configuration;
using ServerApi.Internal;

namespace ServerApi.Kcp;

/// <summary>
/// KCP gateway that manages KCP server and connections.
/// KCP is a UDP-based reliable protocol with better performance than TCP in some scenarios.
/// </summary>
public class KcpGateway : IHostedService
{
    private readonly ServerApiRegistrar _registrar;
    private readonly ILogger<KcpGateway> _logger;
    private readonly KcpOptions _options;
    private readonly SecurityConfig _securityConfig;
    private readonly ConcurrentDictionary<int, KcpConnection> _connections = new();
    private KcpServer? _server;
    private CancellationTokenSource? _cts;
    private Task? _tickTask;

    public KcpGateway(
        ServerApiRegistrar registrar,
        ILogger<KcpGateway> logger,
        IOptions<ServerApiOptions> options)
    {
        _registrar = registrar;
        _logger = logger;
        _options = options.Value.Kcp ?? new KcpOptions();
        _securityConfig = options.Value.Security ?? new SecurityConfig();

        if (_securityConfig.RequireAuthenticatedUser)
        {
            _logger.LogWarning(
                "KCP: RequireAuthenticatedUser is enabled but KCP does not support " +
                "HTTP-based authentication. Consider implementing custom token-based authentication.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Create KCP server with configuration
        var config = new KcpConfig(
            NoDelay: true,           // Enable no-delay mode for lower latency
            Interval: 10,            // Update interval in milliseconds
            FastResend: 2,           // Fast resend
            CongestionWindow: false, // Disable congestion window
            SendWindowSize: 4096,    // Send window size
            ReceiveWindowSize: 4096, // Receive window size
            Timeout: 10000,          // Connection timeout in milliseconds
            MaxRetransmits: 40       // Max retransmit attempts (KCP default)
        );

        _server = new KcpServer(
            OnConnected: OnClientConnected,
            OnData: OnClientData,
            OnDisconnected: OnClientDisconnected,
            OnError: OnError,
            config: config
        );

        _server.Start((ushort)_options.Port);
        _cts = new CancellationTokenSource();

        _logger.LogInformation("KCP gateway listening on port {Port}.", _options.Port);

        // Start KCP tick loop (required for KCP internal updates)
        _tickTask = TickLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KCP gateway stopping...");

        _cts?.Cancel();
        _server?.Stop();

        if (_tickTask != null)
        {
            await _tickTask;
        }

        // Dispose all connections
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
        _connections.Clear();

        _cts?.Dispose();
    }

    private void OnClientConnected(int connectionId)
    {
        try
        {
            var endpoint = _server!.GetClientEndPoint(connectionId);
            var guidConnectionId = Guid.NewGuid().ToString("N");

            _logger.LogInformation("KCP connection {ConnectionId} (#{KcpId}) established from {RemoteEndpoint}.",
                guidConnectionId, connectionId, endpoint);

            var connection = new KcpConnection(
                guidConnectionId,
                connectionId,
                endpoint,
                _server,
                _registrar,
                _logger);

            _connections[connectionId] = connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling KCP client connection.");
        }
    }

    private void OnClientData(int connectionId, ArraySegment<byte> data, KcpChannel channel)
    {
        try
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                // Handle data asynchronously (fire and forget)
                _ = connection.HandleDataAsync(data, channel);
            }
            else
            {
                _logger.LogWarning("Received data from unknown KCP connection #{ConnectionId}.", connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling KCP data from connection #{ConnectionId}.", connectionId);
        }
    }

    private void OnClientDisconnected(int connectionId)
    {
        try
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                _logger.LogInformation("KCP connection {ConnectionId} (#{KcpId}) from {RemoteEndpoint} disconnected.",
                    connection.ConnectionId, connectionId, connection.RemoteEndpoint);
                connection.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling KCP client disconnection.");
        }
    }

    private void OnError(int connectionId, ErrorCode error, string reason)
    {
        _logger.LogWarning("KCP error on connection #{ConnectionId}: {Error} - {Reason}",
            connectionId, error, reason);
    }

    /// <summary>
    /// KCP requires regular Tick() calls to process internal state.
    /// This should be called every 10ms (or according to Interval config).
    /// </summary>
    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _server?.Tick();
                await Task.Delay(10, cancellationToken); // 10ms interval
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in KCP tick loop.");
            }
        }
    }
}
