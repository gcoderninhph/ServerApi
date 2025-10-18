using System.Net.Sockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ClientCore.Protos;

namespace ClientCore.TcpStream;

public class TcpStreamClient : IServerApiClient, IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly string _serverHost;
    private readonly int _serverPort;
    private readonly ILogger<TcpStreamClient> _logger;
    private readonly TcpStreamClientRegister? _register;
    private readonly Dictionary<string, TaskCompletionSource<MessageEnvelope>> _pendingRequests = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public bool IsConnected => _tcpClient.Connected;

    public TcpStreamClient(string serverHost, int serverPort, ILogger<TcpStreamClient> logger, TcpStreamClientRegister? register = null)
    {
        _tcpClient = new TcpClient();
        _serverHost = serverHost;
        _serverPort = serverPort;
        _logger = logger;
        _register = register;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _tcpClient.ConnectAsync(_serverHost, _serverPort, cancellationToken);
            _stream = _tcpClient.GetStream();
            _logger.LogInformation("Connected to TCP server: {Host}:{Port}", _serverHost, _serverPort);

            // Invoke OnConnect callback
            _register?.InvokeOnConnect();

            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to TCP server");
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        _receiveCts?.Cancel();
        _stream?.Close();
        _tcpClient.Close();
        _logger.LogInformation("Disconnected from TCP server");
        
        // Invoke OnDisconnect callback
        _register?.InvokeOnDisconnect();
        
        return Task.CompletedTask;
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(string commandName, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new()
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("TCP client is not connected");

        // ✅ FIX: Use command name as envelope.Id instead of GUID
        var envelope = new MessageEnvelope
        {
            Id = commandName,  // ✅ "Ping", "GetUser", etc. - NOT a GUID!
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(request.ToByteArray())
        };

        // Use command name as correlation key
        var tcs = new TaskCompletionSource<MessageEnvelope>();
        _pendingRequests[commandName] = tcs;

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                var messageBytes = envelope.ToByteArray();
                var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);

                await _stream.WriteAsync(lengthPrefix, 0, 4, cancellationToken);
                await _stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);

                _logger.LogDebug("Sent {CommandName} request", commandName);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var responseEnvelope = await tcs.Task.WaitAsync(linkedCts.Token);

            if (responseEnvelope.Type == MessageType.Error)
            {
                throw new Exception($"Server returned error for command: {commandName}");
            }

            var response = new TResponse();
            response.MergeFrom(responseEnvelope.Data.ToByteArray());
            return response;
        }
        finally
        {
            _pendingRequests.Remove(commandName);
        }
    }

    /// <summary>
    /// Gửi tin nhắn broadcast (one-way) mà không chờ response
    /// </summary>
    internal async Task SendBroadcastAsync(string commandId, byte[] requestBytes, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("TCP client is not connected");

        var envelope = new MessageEnvelope
        {
            Id = commandId,
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(requestBytes)
        };

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var messageBytes = envelope.ToByteArray();
            var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);

            await _stream.WriteAsync(lengthPrefix, 0, 4, cancellationToken);
            await _stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
            await _stream.FlushAsync(cancellationToken);

            _logger.LogDebug("Sent broadcast message for command: {CommandId}", commandId);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream == null)
            return;

        var lengthBuffer = new byte[4];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _tcpClient.Connected)
            {
                // Read length prefix
                var bytesRead = await _stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);
                if (bytesRead != 4)
                {
                    _logger.LogWarning("Connection closed by server");
                    break;
                }

                var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > 1024 * 1024) // 1MB max
                {
                    _logger.LogError("Invalid message length: {Length}", messageLength);
                    break;
                }

                // Read message
                var messageBuffer = new byte[messageLength];
                var totalRead = 0;
                while (totalRead < messageLength)
                {
                    bytesRead = await _stream.ReadAsync(messageBuffer, totalRead, messageLength - totalRead, cancellationToken);
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Connection closed while reading message");
                        return;
                    }
                    totalRead += bytesRead;
                }

                var envelope = MessageEnvelope.Parser.ParseFrom(messageBuffer);

                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                _logger.LogInformation("[{Timestamp}] 📥 RECEIVED envelope: Id={Id}, Type={Type}, DataLength={Length}", 
                    timestamp, envelope.Id, envelope.Type, envelope.Data?.Length ?? 0);

                // Match response by command name (envelope.Id)
                if (_pendingRequests.TryGetValue(envelope.Id, out var tcs))
                {
                    _logger.LogInformation("[{Timestamp}] ✅ Matched pending request", timestamp);
                    tcs.SetResult(envelope);
                }
                else
                {
                    // Server push message - dispatch to registered handler
                    _logger.LogInformation("[{Timestamp}] 📩 SERVER PUSH - Dispatching to handler", timestamp);
                    
                    if (_register != null && envelope.Data != null && envelope.Data.Length > 0)
                    {
                        _register.DispatchServerMessage(envelope.Id, envelope.Data.ToByteArray());
                    }
                    else
                    {
                        _logger.LogWarning("[{Timestamp}] ❌ Cannot dispatch - register null or no data", timestamp);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Receive loop cancelled");
            
            // Invoke OnDisconnect on cancellation
            _register?.InvokeOnDisconnect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive loop");
            
            // Invoke OnDisconnect on error
            _register?.InvokeOnDisconnect();
        }
    }

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveTask?.Wait(TimeSpan.FromSeconds(5));
        _stream?.Dispose();
        _tcpClient.Dispose();
        _receiveCts?.Dispose();
        _sendLock.Dispose();
    }
}
