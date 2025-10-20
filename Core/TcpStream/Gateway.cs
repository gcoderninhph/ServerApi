using System;
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

                // Handle connection in background
                _ = Task.Run(async () =>
                {
                    var connection = new TcpStreamConnection(
                        connectionId,
                        client,
                        _registrar,
                        _logger,
                        _options.BufferSize);

                    await connection.RunAsync(cancellationToken);

                    _logger.LogInformation("TCP connection {ConnectionId} closed.", connectionId);
                }, cancellationToken);
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
