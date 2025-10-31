using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using kcp2k;
using Microsoft.Extensions.Logging;
using ServerApi.Protos;

namespace ClientCore.Kcp;

public class KcpClient : IServerApiClient, IDisposable
{
    private kcp2k.KcpClient? _kcpClient;
    private readonly string _serverHost;
    private readonly int _serverPort;
    private readonly ILogger<KcpClient> _logger;
    private readonly KcpClientRegister? _register;
    private readonly Dictionary<string, TaskCompletionSource<MessageEnvelope>> _pendingRequestsByRequestId = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _reconnectLock = new();
    private bool _isConnected = false;
    private bool _autoReconnectEnabled = false;
    private int _maxReconnectRetries = 0;  // 0 = infinite, > 0 = max retries
    private bool _isDisposing = false;
    private Task? _tickTask;
    private CancellationTokenSource? _tickCts;

    public bool IsConnected => _isConnected && !_isDisposing && _kcpClient != null;

    public KcpClient(string serverHost, int serverPort, ILogger<KcpClient> logger, KcpClientRegister? register = null)
    {
        _serverHost = serverHost;
        _serverPort = serverPort;
        _logger = logger;
        _register = register;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📡 [KCP Connect] Starting connection to {Host}:{Port}...", _serverHost, _serverPort);
        
        try
        {
            // ✅ CRITICAL: Cancel old tick loop BEFORE creating new client
            if (_tickCts != null && !_tickCts.IsCancellationRequested)
            {
                _logger.LogWarning("🛑 [KCP Connect] Cancelling old tick loop...");
                _tickCts.Cancel();
                if (_tickTask != null)
                {
                    try
                    {
                        await _tickTask; // Wait for old tick loop to complete
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelling
                    }
                }
                _tickCts.Dispose();
                _tickCts = null;
                _tickTask = null;
            }

            // Disconnect old client if exists
            if (_kcpClient != null)
            {
                _logger.LogWarning("🛑 [KCP Connect] Disconnecting old client...");
                _kcpClient.Disconnect();
                _kcpClient = null;
            }

            // Resolve hostname to IP
            var addresses = await Dns.GetHostAddressesAsync(_serverHost, cancellationToken);
            if (addresses.Length == 0)
            {
                throw new InvalidOperationException($"Could not resolve hostname: {_serverHost}");
            }

            // Create KCP client with configuration
            var config = new KcpConfig(
                NoDelay: true,
                Interval: 10,
                FastResend: 2,
                CongestionWindow: false,
                SendWindowSize: 4096,
                ReceiveWindowSize: 4096,
                Timeout: 10000,
                MaxRetransmits: 40
            );

            _kcpClient = new kcp2k.KcpClient(
                OnConnected: OnConnected,
                OnData: OnData,
                OnDisconnected: OnDisconnected,
                OnError: OnError,
                config: config
            );

            // Connect to server
            _kcpClient.Connect(addresses[0].ToString(), (ushort)_serverPort);

            // Start NEW tick loop (required for KCP)
            _tickCts = new CancellationTokenSource();
            _tickTask = TickLoopAsync(_tickCts.Token);

            // Wait for connection with timeout (10 seconds)
            var timeoutTask = Task.Delay(10000, cancellationToken);
            var connectedTask = WaitForConnectionAsync(cancellationToken);
            
            var completedTask = await Task.WhenAny(connectedTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogError("❌ [KCP Connect] Connection timeout after 10 seconds");
                throw new TimeoutException("Connection timeout after 10 seconds");
            }

            _logger.LogWarning("✅ [KCP Connect] Connected to KCP server: {Host}:{Port}", _serverHost, _serverPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [KCP Connect] Failed to connect to KCP server");
            throw;
        }
    }

    private async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        while (!_isConnected && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(50, cancellationToken);
        }
    }

    private void OnConnected()
    {
        _isConnected = true;
        _logger.LogInformation("📡 [KCP] OnConnected callback triggered");
        _register?.InvokeOnConnect();
    }

    private void OnData(ArraySegment<byte> data, KcpChannel channel)
    {
        try
        {
            var envelope = MessageEnvelope.Parser.ParseFrom(data.Array, data.Offset, data.Count);

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _logger.LogInformation("[{Timestamp}] 📥 RECEIVED KCP envelope: Id={Id}, RequestId={RequestId}, Type={Type}, DataLength={Length}", 
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
                    _register.DispatchMessage(envelope.Id, envelope.Data.ToByteArray());
                }
                else
                {
                    _logger.LogWarning("[{Timestamp}] ⚠️ No handler registered or empty data", timestamp);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing KCP data");
        }
    }

    private void OnDisconnected()
    {
        _isConnected = false;
        _logger.LogWarning("📡 [KCP] OnDisconnected callback triggered");
        _register?.InvokeOnDisconnect();

        if (_autoReconnectEnabled && !_isDisposing)
        {
            _logger.LogWarning("🔄 [KCP] AutoReconnect enabled - starting reconnect...");
            _ = Task.Run(() => ReconnectWithBackoffAsync());
        }
    }

    private void OnError(ErrorCode error, string reason)
    {
        _logger.LogError("KCP error: {Error} - {Reason}", error, reason);
    }

    public async Task DisconnectAsync()
    {
        _logger.LogInformation("🛑 [KCP Disconnect] Starting disconnect...");
        
        // Cancel tick loop
        if (_tickCts != null && !_tickCts.IsCancellationRequested)
        {
            _tickCts.Cancel();
            if (_tickTask != null)
            {
                try
                {
                    await _tickTask; // Wait for tick loop to complete
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
            }
        }
        
        // Disconnect KCP client
        _kcpClient?.Disconnect();
        _isConnected = false;
        
        _logger.LogInformation("✅ [KCP Disconnect] Disconnected from KCP server");
        
        _register?.InvokeOnDisconnect();
    }

    /// <summary>
    /// Enable/disable auto reconnect when connection lost
    /// </summary>
    public void EnableAutoReconnect(bool enable, int maxRetries = 0)
    {
        _autoReconnectEnabled = enable;
        _maxReconnectRetries = maxRetries;
        _logger.LogWarning("🔧 [KCP AutoReconnect] Set to: {Status} (MaxRetries: {MaxRetries})", 
            enable ? "ENABLED" : "DISABLED", maxRetries == 0 ? "INFINITE" : maxRetries.ToString());
    }

    /// <summary>
    /// Send request with requestId correlation. Timeout: 20s
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new()
    {
        if (!IsConnected || _kcpClient == null)
            throw new InvalidOperationException("KCP client is not connected");

        // Generate unique requestId
        var requestId = Guid.NewGuid().ToString("N");

        var envelope = new MessageEnvelope
        {
            Id = id,
            RequestId = requestId,
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
                var segment = new ArraySegment<byte>(messageBytes);

                _kcpClient.Send(segment, KcpChannel.Reliable);

                _logger.LogDebug("Sent KCP {CommandName} request with RequestId={RequestId}", id, requestId);
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
                    var errorMessage = responseEnvelope.Data.ToStringUtf8();
                    throw new Exception(errorMessage);
                }

                var response = new TResponse();
                response.MergeFrom(responseEnvelope.Data.ToByteArray());
                return response;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Request timeout after 20s for command: {id}, RequestId: {requestId}");
            }
        }
        finally
        {
            _pendingRequestsByRequestId.Remove(requestId);
        }
    }

    /// <summary>
    /// Send broadcast message (one-way)
    /// </summary>
    internal async Task SendBroadcastAsync(string commandId, byte[] requestBytes, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _kcpClient == null)
            throw new InvalidOperationException("KCP client is not connected");

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
            var segment = new ArraySegment<byte>(messageBytes);

            _kcpClient.Send(segment, KcpChannel.Reliable);

            _logger.LogDebug("Sent KCP broadcast message for command: {CommandId}", commandId);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Send fire-and-forget request
    /// </summary>
    public async Task SendFireAndForgetAsync<TRequest>(string commandName, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IMessage
    {
        if (!IsConnected || _kcpClient == null)
            throw new InvalidOperationException("KCP client is not connected");

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
            var segment = new ArraySegment<byte>(messageBytes);

            _kcpClient.Send(segment, KcpChannel.Reliable);

            _logger.LogDebug("Sent KCP fire-and-forget request for command: {CommandName}", commandName);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// KCP requires regular Tick() calls
    /// </summary>
    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_isDisposing)
        {
            try
            {
                _kcpClient?.Tick();
                await Task.Delay(10, cancellationToken); // 10ms interval
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in KCP tick loop");
            }
        }
    }

    private async Task ReconnectWithBackoffAsync()
    {
        if (_isDisposing)
        {
            _logger.LogWarning("🔄 [KCP Reconnect] IsDisposing=true, aborting reconnect");
            return;
        }

        lock (_reconnectLock)
        {
            if (_isDisposing)
            {
                _logger.LogWarning("🔄 [KCP Reconnect] IsDisposing=true (in lock), aborting reconnect");
                return;
            }
        }

        var retryCount = 0;
        var delaySeconds = 1;

        while (retryCount < _maxReconnectRetries || _maxReconnectRetries == 0)
        {
            if (_isDisposing)
            {
                _logger.LogWarning("🔄 [KCP Reconnect] IsDisposing=true, stopping reconnect loop");
                break;
            }

            retryCount++;
            var retryLabel = _maxReconnectRetries == 0 ? $"#{retryCount}" : $"#{retryCount}/{_maxReconnectRetries}";

            _logger.LogWarning("🔄 [KCP Reconnect] Attempt {RetryLabel} after {Delay}s...", retryLabel, delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            if (_isDisposing)
            {
                _logger.LogWarning("🔄 [KCP Reconnect] IsDisposing=true after delay, stopping");
                break;
            }

            try
            {
                _logger.LogDebug("🔄 [KCP Reconnect] Calling ConnectAsync...");
                await ConnectAsync();

                _logger.LogWarning("✅ [KCP Reconnect] Successfully reconnected");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ [KCP Reconnect] Attempt {RetryLabel} failed", retryLabel);
                
                delaySeconds = Math.Min(delaySeconds * 2, 60);
            }
        }

        if (_maxReconnectRetries > 0)
        {
            _logger.LogError("❌ [KCP Reconnect] Max retry attempts ({MaxRetries}) reached. Giving up.", _maxReconnectRetries);
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("🗑️ [KCP Dispose] Starting disposal...");
        
        lock (_reconnectLock)
        {
            _isDisposing = true;
        }

        _tickCts?.Cancel();
        _kcpClient?.Disconnect();
        _sendLock.Dispose();
        _tickCts?.Dispose();
        
        _logger.LogInformation("🗑️ [KCP Dispose] Completed");
    }
}