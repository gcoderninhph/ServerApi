using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using kcp2k;
using Microsoft.Extensions.Logging;
using ServerApi.Abstractions;
using ServerApi.Internal;

namespace ServerApi.Kcp;

/// <summary>
/// Manages a single KCP client connection.
/// </summary>
internal class KcpConnection
{
    private readonly string _connectionId;
    private readonly int _clientConnectionId;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly KcpServer _server;
    private readonly ILogger _logger;
    private readonly ServerApiRegistrar _registrar;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Context? _context;

    public string ConnectionId => _connectionId;

    public KcpConnection(
        string connectionId,
        int clientConnectionId,
        IPEndPoint remoteEndpoint,
        KcpServer server,
        ServerApiRegistrar registrar,
        ILogger logger)
    {
        _connectionId = connectionId;
        _clientConnectionId = clientConnectionId;
        _remoteEndpoint = remoteEndpoint;
        _server = server;
        _registrar = registrar;
        _logger = logger;
        _context = new Context(_connectionId, "Kcp");
    }

    public IPEndPoint RemoteEndpoint => _remoteEndpoint;
    public int ClientConnectionId => _clientConnectionId;

    /// <summary>
    /// Handle incoming KCP data from client.
    /// </summary>
    public async Task HandleDataAsync(ArraySegment<byte> data, KcpChannel channel)
    {
        // Parse protobuf envelope
        Protos.MessageEnvelope envelope;
        try
        {
            envelope = Protos.MessageEnvelope.Parser.ParseFrom(data.Array, data.Offset, data.Count);
        }
        catch (InvalidProtocolBufferException ex)
        {
            _logger.LogWarning(ex, "KCP connection {ConnectionId} received invalid protobuf.", _connectionId);
            var errorEnvelope = MessageEnvelopeFactory.CreateError(null, "Invalid protobuf payload");
            await SendEnvelopeAsync(errorEnvelope, channel);
            return;
        }

        var commandId = envelope.Id ?? string.Empty;
        var requestId = envelope.RequestId ?? string.Empty;
        _context!.CommandId = commandId;

        // Create responder that can send at any time
        var responder = new Responder<object>(env => SendEnvelopeAsync(env, channel), commandId, string.IsNullOrEmpty(requestId) ? null : requestId);

        try
        {
            // Try to find and invoke handler
            var requestBytes = envelope.Data.ToByteArray();
            var handled = await _registrar.TryInvokeKcpAsync(commandId, _context, requestBytes, responder);
            
            if (!handled)
            {
                _logger.LogWarning("No handler registered for command {CommandId}.", commandId);
                var errorEnvelope = MessageEnvelopeFactory.CreateError(envelope, $"Command '{commandId}' not supported");
                await SendEnvelopeAsync(errorEnvelope, channel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler exception for command {CommandId} on connection {ConnectionId}.", commandId, _connectionId);
            var errorEnvelope = MessageEnvelopeFactory.CreateError(envelope, $"Handler error: {ex.Message}");
            await SendEnvelopeAsync(errorEnvelope, channel);
        }
    }

    /// <summary>
    /// Send envelope to client via KCP.
    /// </summary>
    private async Task SendEnvelopeAsync(Protos.MessageEnvelope envelope, KcpChannel channel, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var payloadBytes = envelope.ToByteArray();
            var segment = new ArraySegment<byte>(payloadBytes);

            // Send via KCP (reliable by default)
            _server.Send(_clientConnectionId, segment, channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send KCP packet to {RemoteEndpoint}.", _remoteEndpoint);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
    }
}
