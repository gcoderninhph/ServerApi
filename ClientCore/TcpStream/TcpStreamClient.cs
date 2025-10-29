using System.Net.Sockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServerApi.Protos;

namespace ClientCore.TcpStream;

public class TcpStreamClient : IServerApiClient, IDisposable
{
    private TcpClient _tcpClient;  // Removed readonly for reconnect support
    private readonly string _serverHost;
    private readonly int _serverPort;
    private readonly ILogger<TcpStreamClient> _logger;
    private readonly TcpStreamClientRegister? _register;
    private readonly Dictionary<string, TaskCompletionSource<MessageEnvelope>> _pendingRequestsByRequestId = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _reconnectLock = new();
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _autoReconnectEnabled = false;
    private int _maxReconnectRetries = 0;  // 0 = infinite, > 0 = max retries
    private bool _isDisposing = false;

    public bool IsConnected => _tcpClient.Connected && _stream != null && !_isDisposing;

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
        _logger.LogInformation("📡 [Connect] Starting connection to {Host}:{Port}...", _serverHost, _serverPort);
        
        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        
        try
        {
            // Add connection timeout (10 seconds)
            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            _logger.LogDebug("📡 [Connect] Calling TcpClient.ConnectAsync...");
            await _tcpClient.ConnectAsync(_serverHost, _serverPort, linkedCts.Token);
            
            _logger.LogDebug("📡 [Connect] Getting network stream...");
            _stream = _tcpClient.GetStream();
            _logger.LogWarning("✅ [Connect] Connected to TCP server: {Host}:{Port}", _serverHost, _serverPort);

            // Invoke OnConnect callback
            _logger.LogDebug("📡 [Connect] Invoking OnConnect callback...");
            _register?.InvokeOnConnect();

            _logger.LogInformation("📡 [Connect] Creating CancellationTokenSource and starting ReceiveLoop...");
            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
            _logger.LogWarning("✅ [Connect] ReceiveLoop started");
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("❌ [Connect] Connection timeout after 10 seconds");
            throw new TimeoutException("Connection timeout after 10 seconds");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Connect] Failed to connect to TCP server");
            throw;
        }
        finally
        {
            timeoutCts?.Dispose();
            linkedCts?.Dispose();
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

    /// <summary>
    /// Enable/disable auto reconnect when connection lost
    /// </summary>
    /// <param name="enable">Enable auto-reconnect</param>
    /// <param name="maxRetries">Max retry attempts (0 = infinite, > 0 = max retries)</param>
    public void EnableAutoReconnect(bool enable, int maxRetries = 0)
    {
        _autoReconnectEnabled = enable;
        _maxReconnectRetries = maxRetries;
        _logger.LogWarning("🔧 [AutoReconnect] Set to: {Status} (MaxRetries: {MaxRetries}, Current IsDisposing: {IsDisposing})", 
            enable ? "ENABLED" : "DISABLED", maxRetries == 0 ? "INFINITE" : maxRetries.ToString(), _isDisposing);
    }

    /// <summary>
    /// Send request with requestId correlation. Timeout: 20s
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new()
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("TCP client is not connected");

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
                var messageBytes = envelope.ToByteArray();
                var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);

                await _stream.WriteAsync(lengthPrefix, 0, 4, cancellationToken);
                await _stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);

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

    /// <summary>
    /// Send fire-and-forget request without waiting for response
    /// </summary>
    public async Task SendFireAndForgetAsync<TRequest>(string commandName, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IMessage
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("TCP client is not connected");

        var envelope = new MessageEnvelope
        {
            Id = commandName,
            Type = MessageType.Request,
            Data = Google.Protobuf.ByteString.CopyFrom(request.ToByteArray())
        };

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var messageBytes = envelope.ToByteArray();
            var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);

            await _stream.WriteAsync(lengthPrefix, 0, 4, cancellationToken);
            await _stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
            await _stream.FlushAsync(cancellationToken);

            _logger.LogDebug("Sent fire-and-forget request for command: {CommandName}", commandName);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("🔄 [ReceiveLoop] Started (Connected: {Connected}, IsDisposing: {IsDisposing})", 
            _tcpClient.Connected, _isDisposing);
        
        if (_stream == null)
        {
            _logger.LogError("❌ [ReceiveLoop] Stream is null - exiting");
            return;
        }

        var lengthBuffer = new byte[4];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _tcpClient.Connected)
            {
                _logger.LogDebug("🔄 [ReceiveLoop] Waiting for message...");
                
                // Read length prefix (4 bytes) - loop to ensure we read all 4 bytes
                var totalRead = 0;
                while (totalRead < 4)
                {
                    var bytesRead = await _stream.ReadAsync(lengthBuffer, totalRead, 4 - totalRead, cancellationToken);
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Connection closed by server");
                        return;
                    }
                    totalRead += bytesRead;
                }

                var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // 10MB max (match server)
                {
                    _logger.LogError("Invalid message length: {Length}", messageLength);
                    break;
                }

                // Read message
                var messageBuffer = new byte[messageLength];
                totalRead = 0;
                while (totalRead < messageLength)
                {
                    var bytesRead = await _stream.ReadAsync(messageBuffer, totalRead, messageLength - totalRead, cancellationToken);
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Connection closed while reading message");
                        return;
                    }
                    totalRead += bytesRead;
                }

                var envelope = MessageEnvelope.Parser.ParseFrom(messageBuffer);

                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                _logger.LogInformation("[{Timestamp}] 📥 RECEIVED envelope: Id={Id}, RequestId={RequestId}, Type={Type}, DataLength={Length}", 
                    timestamp, envelope.Id, envelope.RequestId, envelope.Type, envelope.Data?.Length ?? 0);

                // Try to match by requestId (for SendRequestAsync)
                if (!string.IsNullOrEmpty(envelope.RequestId) && _pendingRequestsByRequestId.TryGetValue(envelope.RequestId, out var tcsByRequestId))
                {
                    _logger.LogInformation("[{Timestamp}] ✅ Matched pending request by RequestId", timestamp);
                    tcsByRequestId.SetResult(envelope);
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
            _logger.LogWarning("🛑 [ReceiveLoop] Cancelled (IsDisposing: {IsDisposing}, AutoReconnect: {AutoReconnect})", 
                _isDisposing, _autoReconnectEnabled);
            
            // Cancel all pending requests
            foreach (var kvp in _pendingRequestsByRequestId)
            {
                kvp.Value.TrySetException(new OperationCanceledException("Connection closed"));
            }
            _pendingRequestsByRequestId.Clear();
            
            // Invoke OnDisconnect on cancellation
            _register?.InvokeOnDisconnect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ReceiveLoop] Exception caught (IsDisposing: {IsDisposing}, AutoReconnect: {AutoReconnect}, CancellationRequested: {CancellationRequested})", 
                _isDisposing, _autoReconnectEnabled, cancellationToken.IsCancellationRequested);
            
            // Cancel all pending requests with connection error
            foreach (var kvp in _pendingRequestsByRequestId)
            {
                kvp.Value.TrySetException(new IOException("Connection lost", ex));
            }
            _pendingRequestsByRequestId.Clear();
            
            // Invoke OnDisconnect on error
            _register?.InvokeOnDisconnect();

            // Auto reconnect if enabled
            if (_autoReconnectEnabled && !_isDisposing && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("🔄 [ReceiveLoop] Triggering auto-reconnect...");
                _ = Task.Run(async () => await ReconnectWithBackoffAsync());
            }
            else
            {
                _logger.LogWarning("⏸️ [ReceiveLoop] NOT triggering reconnect (AutoReconnect: {AutoReconnect}, IsDisposing: {IsDisposing}, CancellationRequested: {CancellationRequested})",
                    _autoReconnectEnabled, _isDisposing, cancellationToken.IsCancellationRequested);
            }
        }
    }

    private async Task ReconnectWithBackoffAsync()
    {
        var maxRetries = _maxReconnectRetries; // 0 = infinite
        var maxRetriesDisplay = maxRetries == 0 ? "∞" : maxRetries.ToString();
        
        _logger.LogWarning("🔄 [Reconnect] Started (AutoReconnect: {AutoReconnect}, MaxRetries: {MaxRetries}, IsDisposing: {IsDisposing})", 
            _autoReconnectEnabled, maxRetriesDisplay, _isDisposing);
        
        var retryCount = 0;
        var baseDelay = TimeSpan.FromSeconds(1);

        // Loop: if maxRetries == 0 (infinite), condition is always true
        //       if maxRetries > 0, loop until retryCount >= maxRetries
        while (_autoReconnectEnabled && !_isDisposing && (maxRetries == 0 || retryCount < maxRetries))
        {
            retryCount++;
            var delay = TimeSpan.FromSeconds(Math.Min(baseDelay.TotalSeconds * Math.Pow(2, retryCount - 1), 60));

            _logger.LogWarning("⏳ [Reconnect] Attempt {Retry}/{MaxRetries} after {Delay}s... (AutoReconnect: {AutoReconnect}, IsDisposing: {IsDisposing})", 
                retryCount, maxRetriesDisplay, delay.TotalSeconds, _autoReconnectEnabled, _isDisposing);
            await Task.Delay(delay);

            try
            {
                _logger.LogInformation("🔒 [Reconnect] Acquiring lock...");
                
                // Use lock to prevent race with Dispose
                lock (_reconnectLock)
                {
                    _logger.LogInformation("🔓 [Reconnect] Lock acquired (IsDisposing: {IsDisposing})", _isDisposing);
                    
                    if (_isDisposing)
                    {
                        _logger.LogWarning("⏸️ [Reconnect] Aborting - IsDisposing = true");
                        return;  // Check again inside lock
                    }
                    
                    // Dispose old CancellationTokenSource
                    _logger.LogDebug("🗑️ [Reconnect] Disposing old CancellationTokenSource...");
                    _receiveCts?.Dispose();
                    
                    _logger.LogDebug("🗑️ [Reconnect] Disposing old TcpClient...");
                    _tcpClient.Dispose();
                    _tcpClient = new TcpClient();
                    _logger.LogDebug("✅ [Reconnect] Created new TcpClient");
                }

                _logger.LogInformation("📡 [Reconnect] Calling ConnectAsync...");
                await ConnectAsync();
                _logger.LogWarning("✅ [Reconnect] ConnectAsync succeeded after {Retry} attempts! (ReceiveLoop already started by ConnectAsync)", retryCount);
                
                // ✅ KHÔNG CẦN start ReceiveLoop ở đây - ConnectAsync() đã làm rồi!
                // ConnectAsync() đã:
                // 1. Create new CancellationTokenSource
                // 2. Start ReceiveLoopAsync()
                // 3. Invoke OnConnect callback
                
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [Reconnect] Attempt {Retry}/{MaxRetries} failed (AutoReconnect: {AutoReconnect}, IsDisposing: {IsDisposing})", 
                    retryCount, maxRetriesDisplay, _autoReconnectEnabled, _isDisposing);
            }
        }

        _logger.LogError("💀 [Reconnect] Failed to reconnect after {Retry} attempts (MaxRetries: {MaxRetries}, AutoReconnect: {AutoReconnect}, IsDisposing: {IsDisposing})", 
            retryCount, maxRetriesDisplay, _autoReconnectEnabled, _isDisposing);
    }

    public void Dispose()
    {
        _logger.LogWarning("🗑️ [Dispose] Called - Acquiring lock...");
        
        lock (_reconnectLock)
        {
            _logger.LogWarning("🗑️ [Dispose] Lock acquired - Setting IsDisposing = true");
            _isDisposing = true;
        }
        
        _logger.LogInformation("🗑️ [Dispose] Cancelling receive CancellationTokenSource...");
        _receiveCts?.Cancel();
        
        // Don't wait for receive task - close stream to unblock any pending reads
        _logger.LogInformation("🗑️ [Dispose] Closing stream...");
        try
        {
            _stream?.Close();
        }
        catch { }
        
        _logger.LogInformation("🗑️ [Dispose] Closing TcpClient...");
        try
        {
            _tcpClient.Close();
        }
        catch { }
        
        _logger.LogInformation("🗑️ [Dispose] Disposing resources...");
        _receiveCts?.Dispose();
        _sendLock.Dispose();
        _logger.LogWarning("✅ [Dispose] Complete");
    }
}
