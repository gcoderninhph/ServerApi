using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServerApi.Abstractions;
using ServerApi.Internal;
using WS = System.Net.WebSockets.WebSocket;

namespace ServerApi.WebSocket;

/// <summary>
/// Manages a single WebSocket client connection. Clean and simple.
/// </summary>
internal class WebSocketConnection
{
    private readonly string _connectionId;
    private readonly WS _socket;
    private readonly ILogger _logger;
    private readonly ServerApiRegistrar _registrar;
    private readonly ClaimsPrincipal? _user;
    private readonly IReadOnlyDictionary<string, string>? _headers;
    private readonly IReadOnlyDictionary<string, string>? _queryParameters;
    private readonly int _bufferSize;
    private Context? _context;

    public WebSocketConnection(
        string connectionId,
        WS socket,
        ServerApiRegistrar registrar,
        ILogger logger,
        int bufferSize,
        ClaimsPrincipal? user = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? queryParameters = null)
    {
        _connectionId = connectionId;
        _socket = socket;
        _registrar = registrar;
        _logger = logger;
        _bufferSize = bufferSize;
        _user = user;
        _headers = headers;
        _queryParameters = queryParameters;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Create context once for this connection
        _context = new Context(_connectionId, "WebSocket")
        {
            User = _user,
            Headers = _headers,
            QueryParameters = _queryParameters
        };

        // Register this connection for broadcasting
        _registrar.ConnectionRegistry.RegisterWebSocket(_connectionId, async bytes => await SendBytesAsync(bytes, CancellationToken.None));

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(cancellationToken);
                if (message == null)
                    break;

                if (message.Value.MessageType == WebSocketMessageType.Close)
                    break;

                if (message.Value.MessageType != WebSocketMessageType.Binary)
                {
                    _logger.LogWarning("Connection {ConnectionId} received non-binary message.", _connectionId);
                    await CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Binary required", cancellationToken);
                    return;
                }

                await HandleMessageAsync(message.Value.Payload, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error on connection {ConnectionId}.", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on connection {ConnectionId}.", _connectionId);
        }
        finally
        {
            // Unregister this connection
            _registrar.ConnectionRegistry.UnregisterWebSocket(_connectionId);
            
            if (_socket.State == WebSocketState.Open)
            {
                await CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
            }
            _socket.Dispose();
        }
    }

    private async Task HandleMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        Protos.MessageEnvelope envelope;
        try
        {
            envelope = Protos.MessageEnvelope.Parser.ParseFrom(payload.ToArray());
        }
        catch (InvalidProtocolBufferException ex)
        {
            _logger.LogWarning(ex, "Connection {ConnectionId} received invalid protobuf.", _connectionId);
            var errorEnvelope = MessageEnvelopeFactory.CreateError(null, "Invalid protobuf payload");
            await SendEnvelopeAsync(errorEnvelope, cancellationToken);
            return;
        }

        var commandId = envelope.Id ?? string.Empty;
        var requestId = envelope.RequestId ?? string.Empty;
        _context!.CommandId = commandId;

        // Create responder that can send at any time, include requestId for correlation
        var responder = new Responder<object>(env => SendEnvelopeAsync(env), commandId, string.IsNullOrEmpty(requestId) ? null : requestId);

        // Try to find and invoke handler
        var requestBytes = envelope.Data.ToByteArray();
        var handled = await _registrar.TryInvokeWebSocketAsync(commandId, _context, requestBytes, responder);
        
        if (!handled)
        {
            _logger.LogWarning("No handler registered for command {CommandId}.", commandId);
            var errorEnvelope = MessageEnvelopeFactory.CreateError(envelope, $"Command '{commandId}' not supported");
            await SendEnvelopeAsync(errorEnvelope, cancellationToken);
        }
    }

    private async Task<(WebSocketMessageType MessageType, ReadOnlyMemory<byte> Payload)?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[_bufferSize];
        WebSocketReceiveResult result;

        do
        {
            var segment = new ArraySegment<byte>(buffer);
            result = await _socket.ReceiveAsync(segment, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, ReadOnlyMemory<byte>.Empty);
            }

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return (result.MessageType, ms.ToArray());
    }

    private async Task SendEnvelopeAsync(Protos.MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var bytes = envelope.ToByteArray();
        await SendBytesAsync(bytes, cancellationToken);
    }

    private async Task SendBytesAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (_socket.State != WebSocketState.Open)
            return;

        await _socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken);
    }

    private async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        try
        {
            await _socket.CloseAsync(status, description, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing WebSocket connection {ConnectionId}.", _connectionId);
        }
    }
}
