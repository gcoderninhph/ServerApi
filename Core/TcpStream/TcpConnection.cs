using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServerApi.Abstractions;
using ServerApi.Internal;

namespace ServerApi.TcpStream;

/// <summary>
/// Manages a single TCP Stream client connection. Clean and simple.
/// </summary>
internal class TcpStreamConnection
{
    private readonly string _connectionId;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ILogger _logger;
    private readonly ServerApiRegistrar _registrar;
    private readonly int _bufferSize;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Context? _context;

    public TcpStreamConnection(
        string connectionId,
        TcpClient client,
        ServerApiRegistrar registrar,
        ILogger logger,
        int bufferSize)
    {
        _connectionId = connectionId;
        _client = client;
        _stream = client.GetStream();
        _registrar = registrar;
        _logger = logger;
        _bufferSize = bufferSize;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Create context once for this connection
        _context = new Context(_connectionId, "TcpStream");

        try
        {
            while (!cancellationToken.IsCancellationRequested && _client.Connected)
            {
                var message = await ReceiveMessageAsync(cancellationToken);
                if (message == null)
                    break;

                await HandleMessageAsync(message.Value, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "TCP Stream I/O error on connection {ConnectionId}.", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on connection {ConnectionId}.", _connectionId);
        }
        finally
        {
            _stream.Close();
            _client.Close();
            _sendLock.Dispose();
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

        try
        {
            // Try to find and invoke handler
            var requestBytes = envelope.Data.ToByteArray();
            var handled = await _registrar.TryInvokeTcpStreamAsync(commandId, _context, requestBytes, responder);
            
            if (!handled)
            {
                _logger.LogWarning("No handler registered for command {CommandId}.", commandId);
                var errorEnvelope = MessageEnvelopeFactory.CreateError(envelope, $"Command '{commandId}' not supported");
                await SendEnvelopeAsync(errorEnvelope, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler exception for command {CommandId} on connection {ConnectionId}.", commandId, _connectionId);
            var errorEnvelope = MessageEnvelopeFactory.CreateError(envelope, $"Handler error: {ex.Message}");
            await SendEnvelopeAsync(errorEnvelope, cancellationToken);
        }
    }

    private async Task<ReadOnlyMemory<byte>?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        // Read length prefix (4 bytes)
        var lengthBytes = new byte[4];
        var bytesRead = 0;
        while (bytesRead < 4)
        {
            var read = await _stream.ReadAsync(lengthBytes.AsMemory(bytesRead, 4 - bytesRead), cancellationToken);
            if (read == 0)
                return null;
            bytesRead += read;
        }

        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > 10 * 1024 * 1024) // 10MB max
        {
            _logger.LogWarning("Connection {ConnectionId} sent invalid message length: {Length}", _connectionId, length);
            return null;
        }

        // Read message body
        var messageBytes = new byte[length];
        bytesRead = 0;
        while (bytesRead < length)
        {
            var read = await _stream.ReadAsync(messageBytes.AsMemory(bytesRead, length - bytesRead), cancellationToken);
            if (read == 0)
                return null;
            bytesRead += read;
        }

        return messageBytes;
    }

    private async Task SendEnvelopeAsync(Protos.MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_client.Connected)
            return;

        var bytes = envelope.ToByteArray();
        var lengthBytes = BitConverter.GetBytes(bytes.Length);

        // Use lock to prevent race condition when multiple handlers send concurrently
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            // Write length prefix + message
            await _stream.WriteAsync(lengthBytes, cancellationToken);
            await _stream.WriteAsync(bytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
