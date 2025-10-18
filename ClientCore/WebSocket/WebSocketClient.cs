using System.Net.WebSockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ClientCore.Protos;

namespace ClientCore.WebSocket;

public class WebSocketClient : IServerApiClient, IDisposable
{
    private readonly ClientWebSocket _webSocket;
    private readonly string _serverUri;
    private readonly ILogger<WebSocketClient> _logger;
    private readonly WebSocketClientRegister? _register;
    private readonly Dictionary<string, TaskCompletionSource<MessageEnvelope>> _pendingRequests = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public bool IsConnected => _webSocket.State == WebSocketState.Open;

    public WebSocketClient(string serverUri, ILogger<WebSocketClient> logger, WebSocketClientRegister? register = null)
    {
        _webSocket = new ClientWebSocket();
        _serverUri = serverUri;
        _logger = logger;
        _register = register;
    }

    /// <summary>
    /// Thêm header vào WebSocket request (phải gọi trước ConnectAsync)
    /// </summary>
    public void AddHeader(string name, string value)
    {
        if (_webSocket.State != WebSocketState.None)
        {
            throw new InvalidOperationException("Cannot add headers after connection is established");
        }

        _webSocket.Options.SetRequestHeader(name, value);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _webSocket.ConnectAsync(new Uri(_serverUri), cancellationToken);
            _logger.LogInformation("✅ Connected to WebSocket server: {ServerUri}", _serverUri);

            // Invoke OnConnect callback
            _register?.InvokeOnConnect();

            _receiveCts = new CancellationTokenSource();
            _logger.LogInformation("🚀 Starting receive loop task...");
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
            _logger.LogInformation("✅ Receive loop task started: TaskId={TaskId}", _receiveTask.Id);
            
            // Monitor the task for unhandled exceptions
            _ = _receiveTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "❌ RECEIVE LOOP TASK FAULTED!");
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to WebSocket server");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
        {
            _receiveCts?.Cancel();
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
            _logger.LogInformation("Disconnected from WebSocket server");
            
            // Invoke OnDisconnect callback
            _register?.InvokeOnDisconnect();
        }
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(string commandName, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new()
    {
        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        // ✅ FIX: Use command name as envelope.Id instead of GUID
        // This matches server's expectation for routing to handlers
        var envelope = new MessageEnvelope
        {
            Id = commandName,  // ✅ "Ping", "GetUser", etc. - NOT a GUID!
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(request.ToByteArray())
        };

        // Use command name as correlation key
        // Note: This means only ONE request per command at a time
        // TODO: Add proper correlation ID mechanism for concurrent requests
        var tcs = new TaskCompletionSource<MessageEnvelope>();
        _pendingRequests[commandName] = tcs;

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                var buffer = envelope.ToByteArray();
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, cancellationToken);
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
        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var envelope = new MessageEnvelope
        {
            Id = commandId,
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(requestBytes)
        };

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var buffer = envelope.ToByteArray();
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, cancellationToken);
            _logger.LogDebug("Sent broadcast message for command: {CommandId}", commandId);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔄 CLIENT RECEIVE LOOP STARTED");
        
        var buffer = new byte[8192];
        var memoryStream = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                _logger.LogDebug("⏳ Waiting for WebSocket message... State={State}", _webSocket.State);
                
                memoryStream.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    _logger.LogDebug("📦 Received {Count} bytes, EndOfMessage={End}", result.Count, result.EndOfMessage);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        _logger.LogInformation("WebSocket closed by server");
                        
                        // Invoke OnDisconnect callback when server closes
                        _register?.InvokeOnDisconnect();
                        return;
                    }

                    memoryStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var messageBytes = memoryStream.ToArray();
                _logger.LogInformation("📥 RECEIVED {Size} bytes total, parsing envelope...", messageBytes.Length);
                
                var envelope = MessageEnvelope.Parser.ParseFrom(messageBytes);

                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                _logger.LogInformation("[{Timestamp}] 📥 RECEIVED envelope: Id={Id}, Type={Type}, DataLength={Length}", 
                    timestamp, envelope.Id, envelope.Type, envelope.Data?.Length ?? 0);

                // Match response by command name (envelope.Id)
                if (_pendingRequests.TryGetValue(envelope.Id, out var tcs))
                {
                    _logger.LogInformation("[{Timestamp}] ✅ Matched pending request", timestamp);
                    
                    // Run SetResult on ThreadPool to avoid blocking receive loop
                    var envelopeCopy = envelope;
                    _ = Task.Run(() => tcs.SetResult(envelopeCopy));
                    
                    _logger.LogInformation("[{Timestamp}] 🔄 Scheduled pending request completion, continuing loop...", timestamp);
                }
                else
                {
                    // Server push message - dispatch to registered handler
                    _logger.LogInformation("[{Timestamp}] 📩 SERVER PUSH - Dispatching to handler", timestamp);
                    
                    if (_register != null && envelope.Data != null && envelope.Data.Length > 0)
                    {
                        // Run dispatch on ThreadPool to avoid blocking receive loop
                        var commandId = envelope.Id;
                        var dataBytes = envelope.Data.ToByteArray();
                        _ = Task.Run(() => _register.DispatchServerMessage(commandId, dataBytes));
                    }
                    else
                    {
                        _logger.LogWarning("[{Timestamp}] ❌ Cannot dispatch - register null or no data", timestamp);
                    }
                    
                    _logger.LogInformation("[{Timestamp}] 🔄 Scheduled dispatch, continuing loop...", timestamp);
                }
                
                _logger.LogDebug("🔁 LOOP ITERATION COMPLETE - Going back to wait for next message");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🛑 CLIENT RECEIVE LOOP CANCELLED");
            
            // Invoke OnDisconnect on cancellation
            _register?.InvokeOnDisconnect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ CLIENT RECEIVE LOOP ERROR");
            
            // Invoke OnDisconnect on error
            _register?.InvokeOnDisconnect();
        }
        finally
        {
            _logger.LogInformation("🔴 CLIENT RECEIVE LOOP ENDED");
        }
    }

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveTask?.Wait(TimeSpan.FromSeconds(5));
        _webSocket.Dispose();
        _receiveCts?.Dispose();
        _sendLock.Dispose();
    }
}
