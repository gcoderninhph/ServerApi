using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace ClientCore.Kcp;

public class KcpClientRegister : IKcpClientRegister
{
    private readonly ConcurrentDictionary<string, RegisteredHandler> _handlers;
    private readonly ILogger<KcpClientRegister> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Action? _onConnectHandler;
    private Action? _onDisconnectHandler;
    private KcpClient? _client;
    private bool _autoReconnectEnabled = false;
    private int _maxReconnectRetries = 0;

    public bool IsConnected => _client?.IsConnected ?? false;
    public KcpClient? Client => _client;

    public KcpClientRegister(ILogger<KcpClientRegister> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _handlers = new ConcurrentDictionary<string, RegisteredHandler>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task ConnectAsync(string host, ushort port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or whitespace.", nameof(host));
        }

        var clientLogger = _loggerFactory.CreateLogger<KcpClient>();
        _client = new KcpClient(host, port, clientLogger, this);

        // Apply auto-reconnect setting if it was set before ConnectAsync
        if (_autoReconnectEnabled)
        {
            _logger.LogInformation("🔧 [KCP Register.ConnectAsync] Applying stored AutoReconnect setting: {Enabled}, MaxRetries: {MaxRetries}", 
                _autoReconnectEnabled, _maxReconnectRetries == 0 ? "INFINITE" : _maxReconnectRetries.ToString());
            _client.EnableAutoReconnect(_autoReconnectEnabled, _maxReconnectRetries);
        }

        await _client.ConnectAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisconnectAsync();
            _client = null;
        }
    }

    public IRequester<TRequest> Register<TRequest, TResponse>(
        string id,
        Action<TResponse> handler,
        Action<string> errorHandler)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new()
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Command ID cannot be null or whitespace.", nameof(id));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (errorHandler == null)
        {
            throw new ArgumentNullException(nameof(errorHandler));
        }

        // Create requester with Func to get client dynamically
        var requester = new Requester<TRequest, TResponse>(() => _client, id);

        // Store handlers to process TResponse received from server
        var registeredHandler = new RegisteredHandler
        {
            RequestType = typeof(TRequest),
            ResponseType = typeof(TResponse),
            Handler = handler,
            ErrorHandler = errorHandler
        };

        if (!_handlers.TryAdd(id, registeredHandler))
        {
            throw new InvalidOperationException($"Handler for command '{id}' is already registered.");
        }

        _logger.LogInformation("Registered KCP client handler for command: {CommandId}", id);
        
        return requester;
    }

    public IBroadcastRequester<TRequest> GetBroadcaster<TRequest>(string commandId)
        where TRequest : class, IMessage
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new ArgumentException("Command ID cannot be null or whitespace.", nameof(commandId));
        }

        if (_client == null)
        {
            throw new InvalidOperationException("KCP client is not connected. Call ConnectAsync first.");
        }

        return new Internal.BroadcastRequester<TRequest>(
            commandId,
            (cmdId, requestBytes, ct) => _client.SendBroadcastAsync(cmdId, requestBytes, ct)
        );
    }

    public void OnConnect(Action handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _onConnectHandler = handler;
        _logger.LogInformation("Registered OnConnect handler for KCP client");
    }

    public void OnDisconnect(Action handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _onDisconnectHandler = handler;
        _logger.LogInformation("Registered OnDisconnect handler for KCP client");
    }

    public void AutoReconnect(bool enable, int maxRetries = 0)
    {
        _autoReconnectEnabled = enable;
        _maxReconnectRetries = maxRetries;
        
        _logger.LogInformation("🔧 [KCP Register.AutoReconnect] Storing setting: {Enabled}, MaxRetries: {MaxRetries} (Client exists: {ClientExists})", 
            enable ? "ENABLED" : "DISABLED", 
            maxRetries == 0 ? "INFINITE" : maxRetries.ToString(),
            _client != null);

        // If client already exists, apply immediately
        if (_client != null)
        {
            _logger.LogInformation("🔧 [KCP Register.AutoReconnect] Applying immediately to existing client");
            _client.EnableAutoReconnect(enable, maxRetries);
        }
    }

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new()
    {
        if (_client == null)
            throw new InvalidOperationException("KCP client is not connected. Call ConnectAsync first.");

        return await _client.SendRequestAsync<TRequest, TResponse>(id, request, cancellationToken);
    }

    /// <summary>
    /// Called by KcpClient when connection is established
    /// </summary>
    internal void InvokeOnConnect()
    {
        try
        {
            _onConnectHandler?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnect handler");
        }
    }

    /// <summary>
    /// Called by KcpClient when connection is lost
    /// </summary>
    internal void InvokeOnDisconnect()
    {
        try
        {
            _onDisconnectHandler?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnDisconnect handler");
        }
    }

    /// <summary>
    /// Dispatch message from server to registered handler
    /// </summary>
    internal void DispatchMessage(string commandId, byte[] responseData)
    {
        if (!_handlers.TryGetValue(commandId, out var registeredHandler))
        {
            _logger.LogWarning("No handler registered for command: {CommandId}", commandId);
            return;
        }

        try
        {
            // Parse response as TResponse
            var responseInstance = (IMessage)Activator.CreateInstance(registeredHandler.ResponseType)!;
            responseInstance.MergeFrom(responseData);

            // Invoke handler with typed response
            registeredHandler.Handler.DynamicInvoke(responseInstance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching message for command: {CommandId}", commandId);
            
            // Invoke error handler if available
            registeredHandler.ErrorHandler?.DynamicInvoke($"Error processing response: {ex.Message}");
        }
    }

    private class RegisteredHandler
    {
        public Type RequestType { get; set; } = null!;
        public Type ResponseType { get; set; } = null!;
        public Delegate Handler { get; set; } = null!;
        public Delegate? ErrorHandler { get; set; }
    }
}
