using System.Net.WebSockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServerApi.Protos;

namespace ClientCore.WebSocket;

public class WebSocketClient : IServerApiClient, IDisposable
{
    private readonly ClientWebSocket _webSocket;
    private readonly string _serverUri;
    private readonly ILogger<WebSocketClient> _logger;
    private readonly WebSocketClientRegister? _register;
    private readonly Dictionary<string, TaskCompletionSource<MessageEnvelope>> _pendingRequestsByRequestId = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _autoReconnectEnabled = false;
    private int _maxReconnectRetries = 0;  // 0 = infinite, > 0 = max retries
    private bool _isDisposing = false;

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

    /// <summary>
    /// Enable/disable auto reconnect when connection lost
    /// </summary>
    /// <param name="enable">Enable auto-reconnect</param>
    /// <param name="maxRetries">Max retry attempts (0 = infinite, > 0 = max retries)</param>
    public void EnableAutoReconnect(bool enable, int maxRetries = 0)
    {
        _autoReconnectEnabled = enable;
        _maxReconnectRetries = maxRetries;
        _logger.LogInformation("Auto reconnect {Status}, MaxRetries: {MaxRetries}", 
            enable ? "enabled" : "disabled", maxRetries == 0 ? "INFINITE" : maxRetries.ToString());
    }

    /// <summary>
    /// Send request with requestId correlation. Timeout: 20s
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new()
    {
        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        // Generate unique requestId
        var requestId = Guid.NewGuid().ToString("N");

        var envelope = new MessageEnvelope
        {
            Id = id,  // Command name
            RequestId = requestId,  // Correlation ID
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(request.ToByteArray())
        };

        var tcs = new TaskCompletionSource<MessageEnvelope>();
        _pendingRequestsByRequestId[requestId] = tcs;

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                var buffer = envelope.ToByteArray();
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, cancellationToken);
                _logger.LogDebug("Sent {CommandName} request with RequestId={RequestId}", id, requestId);
            }
            finally
            {
                _sendLock.Release();
            }

            // Wait for response with timeout (20 seconds)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                var responseEnvelope = await tcs.Task.WaitAsync(timeoutCts.Token);

                if (responseEnvelope.Type == MessageType.Error)
                {
                    // Extract error message from data
                    var errorMessage = responseEnvelope.Data.ToStringUtf8();
                    throw new Exception(errorMessage);
                }

                var response = new TResponse();
                response.MergeFrom(responseEnvelope.Data.ToByteArray());
                return response;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred
                throw new TimeoutException($"Request timeout after 20s for command: {id}, RequestId: {requestId}");
            }
        }
        finally
        {
            _pendingRequestsByRequestId.Remove(requestId);
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

    /// <summary>
    /// Send fire-and-forget request without waiting for response
    /// </summary>
    public async Task SendFireAndForgetAsync<TRequest>(string commandName, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IMessage
    {
        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var envelope = new MessageEnvelope
        {
            Id = commandName,
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(request.ToByteArray())
        };

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var buffer = envelope.ToByteArray();
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, cancellationToken);
            _logger.LogDebug("Sent fire-and-forget request for command: {CommandName}", commandName);
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
                _logger.LogInformation("[{Timestamp}] 📥 RECEIVED envelope: Id={Id}, RequestId={RequestId}, Type={Type}, DataLength={Length}", 
                    timestamp, envelope.Id, envelope.RequestId, envelope.Type, envelope.Data?.Length ?? 0);

                // Try to match by requestId (for SendRequestAsync)
                if (!string.IsNullOrEmpty(envelope.RequestId) && _pendingRequestsByRequestId.TryGetValue(envelope.RequestId, out var tcsByRequestId))
                {
                    _logger.LogInformation("[{Timestamp}] ✅ Matched pending request by RequestId", timestamp);
                    
                    var envelopeCopy = envelope;
                    _ = Task.Run(() => tcsByRequestId.SetResult(envelopeCopy));
                    
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

            // Auto reconnect if enabled
            if (_autoReconnectEnabled && !_isDisposing && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Connection lost, attempting to reconnect...");
                _ = Task.Run(async () => await ReconnectWithBackoffAsync());
            }
        }
        finally
        {
            _logger.LogInformation("🔴 CLIENT RECEIVE LOOP ENDED");
        }
    }

    private async Task ReconnectWithBackoffAsync()
    {
        var maxRetries = _maxReconnectRetries; // 0 = infinite
        var maxRetriesDisplay = maxRetries == 0 ? "∞" : maxRetries.ToString();
        
        _logger.LogWarning("🔄 [WS.Reconnect] Started (AutoReconnect: {AutoReconnect}, MaxRetries: {MaxRetries}, IsDisposing: {IsDisposing})", 
            _autoReconnectEnabled, maxRetriesDisplay, _isDisposing);
        
        var retryCount = 0;
        var baseDelay = TimeSpan.FromSeconds(1);

        // Loop: if maxRetries == 0 (infinite), condition is always true
        //       if maxRetries > 0, loop until retryCount >= maxRetries
        while (_autoReconnectEnabled && !_isDisposing && (maxRetries == 0 || retryCount < maxRetries))
        {
            retryCount++;
            var delay = TimeSpan.FromSeconds(Math.Min(baseDelay.TotalSeconds * Math.Pow(2, retryCount - 1), 60));

            _logger.LogWarning("⏳ [WS.Reconnect] Attempt {Retry}/{MaxRetries} after {Delay}s...", 
                retryCount, maxRetriesDisplay, delay.TotalSeconds);
            await Task.Delay(delay);

            try
            {
                // Create new WebSocket instance
                var fieldInfo = typeof(WebSocketClient).GetField("_webSocket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fieldInfo != null)
                {
                    var oldWebSocket = (ClientWebSocket)fieldInfo.GetValue(this)!;
                    oldWebSocket.Dispose();
                    
                    // Dispose old CancellationTokenSource
                    _receiveCts?.Dispose();
                    
                    var newWebSocket = new ClientWebSocket();
                    fieldInfo.SetValue(this, newWebSocket);
                }

                await ConnectAsync();
                _logger.LogWarning("✅ [WS.Reconnect] ConnectAsync succeeded after {Retry} attempts! (ReceiveLoop already started by ConnectAsync)", retryCount);
                
                // ✅ KHÔNG CẦN start ReceiveLoop ở đây - ConnectAsync() đã làm rồi!
                // ConnectAsync() đã:
                // 1. Create new CancellationTokenSource
                // 2. Start ReceiveLoopAsync()
                // 3. Invoke OnConnect callback
                
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [WS.Reconnect] Attempt {Retry}/{MaxRetries} failed", 
                    retryCount, maxRetriesDisplay);
            }
        }

        _logger.LogError("💀 [WS.Reconnect] Failed to reconnect after {Retry} attempts (MaxRetries: {MaxRetries})", 
            retryCount, maxRetriesDisplay);
    }

    public void Dispose()
    {
        _isDisposing = true;
        _receiveCts?.Cancel();
        _receiveTask?.Wait(TimeSpan.FromSeconds(5));
        _webSocket.Dispose();
        _receiveCts?.Dispose();
        _sendLock.Dispose();
    }
}
