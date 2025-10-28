using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerApi.Configuration;
using ServerApi.Internal;

namespace ServerApi.TcpStream;

/// <summary>
/// TCP Stream gateway that listens for TCP connections and manages them.
/// </summary>
public class TcpGateway : IHostedService
{
    private readonly ServerApiRegistrar _registrar;
    private readonly ILogger<TcpGateway> _logger;
    private readonly TcpStreamOptions _options;
    private readonly SecurityConfig _securityConfig;
    private readonly ConcurrentDictionary<string, Task> _activeConnections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public TcpGateway(
        ServerApiRegistrar registrar,
        ILogger<TcpGateway> logger,
        IOptions<ServerApiOptions> options)
    {
        _registrar = registrar;
        _logger = logger;
        _options = options.Value.TcpStream ?? new TcpStreamOptions();
        _securityConfig = options.Value.Security ?? new SecurityConfig();
        
        // Log warning if authentication is required for TCP Stream
        if (_securityConfig.RequireAuthenticatedUser)
        {
            _logger.LogWarning(
                "TCP Stream: RequireAuthenticatedUser is enabled but TCP Stream does not support " +
                "HTTP-based authentication. Consider implementing custom token-based authentication " +
                "or use WebSocket transport for authenticated connections.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, _options.Port);
        _listener.Start();
        _cts = new CancellationTokenSource();

        _logger.LogInformation("TCP Stream gateway listening on port {Port}.", _options.Port);

        _acceptTask = AcceptConnectionsAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TCP Stream gateway stopping...");

        _cts?.Cancel();
        _listener?.Stop();

        if (_acceptTask != null)
        {
            await _acceptTask;
        }

        // Wait for all active connections to close (graceful shutdown)
        if (_activeConnections.Any())
        {
            _logger.LogInformation("Waiting for {Count} active connections to close...", _activeConnections.Count);
            try
            {
                await Task.WhenAll(_activeConnections.Values);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Some connections closed with errors");
            }
        }

        _cts?.Dispose();
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                var connectionId = Guid.NewGuid().ToString("N");

                _logger.LogInformation("TCP connection {ConnectionId} established from {RemoteEndpoint}.", 
                    connectionId, client.Client.RemoteEndPoint);

                // Handle connection in background and track it
                var connectionTask = Task.Run(async () =>
                {
                    try
                    {
                        var connection = new TcpStreamConnection(
                            connectionId,
                            client,
                            _registrar,
                            _logger,
                            _options.BufferSize);

                        await connection.RunAsync(cancellationToken);

                        _logger.LogInformation("TCP connection {ConnectionId} closed.", connectionId);
                    }
                    finally
                    {
                        _activeConnections.TryRemove(connectionId, out _);
                    }
                }, cancellationToken);

                _activeConnections[connectionId] = connectionTask;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting TCP connection.");
            }
        }
    }
}
