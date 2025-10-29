using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace ClientCore.WebSocket;

public class WebSocketClientRegister : IWebSocketClientRegister
{
    private readonly ConcurrentDictionary<string, RegisteredHandler> _handlers;
    private readonly ILogger<WebSocketClientRegister> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Action? _onConnectHandler;
    private Action? _onDisconnectHandler;
    private WebSocketClient? _client;
    private bool _autoReconnectEnabled = false;  // Store auto-reconnect setting
    private int _maxReconnectRetries = 0;  // Store max retries (0 = infinite)

    public bool IsConnected => _client?.IsConnected ?? false;
    public WebSocketClient? Client => _client;

    public WebSocketClientRegister(ILogger<WebSocketClientRegister> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _handlers = new ConcurrentDictionary<string, RegisteredHandler>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task ConnectAsync(
        string url,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or whitespace.", nameof(url));
        }

        // Build URL with query parameters
        var finalUrl = url;
        if (queryParameters != null && queryParameters.Count > 0)
        {
            var queryString = string.Join("&", queryParameters.Select(kvp => 
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            finalUrl = url.Contains("?") ? $"{url}&{queryString}" : $"{url}?{queryString}";
        }

        _logger.LogInformation("🔧 Creating new WebSocketClient for {Url}", finalUrl);
        var clientLogger = _loggerFactory.CreateLogger<WebSocketClient>();
        _client = new WebSocketClient(finalUrl, clientLogger, this);

        // Add headers if provided
        if (headers != null)
        {
            foreach (var header in headers)
            {
                try
                {
                    _client.AddHeader(header.Key, header.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add header {HeaderName}", header.Key);
                }
            }
        }

        // Apply auto-reconnect setting if it was set before ConnectAsync
        if (_autoReconnectEnabled)
        {
            _logger.LogInformation("🔧 [WS.Register.ConnectAsync] Applying stored AutoReconnect setting: {Enabled}, MaxRetries: {MaxRetries}", 
                _autoReconnectEnabled, _maxReconnectRetries == 0 ? "INFINITE" : _maxReconnectRetries.ToString());
            _client.EnableAutoReconnect(_autoReconnectEnabled, _maxReconnectRetries);
        }

        _logger.LogInformation("📞 Calling _client.ConnectAsync()...");
        await _client.ConnectAsync(cancellationToken);
        _logger.LogInformation("✅ _client.ConnectAsync() completed");
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

        // ✅ Tạo requester với Func để lấy client động (luôn lấy _client hiện tại)
        // Khi reconnect tạo client mới, requester vẫn hoạt động với client mới
        var requester = new Requester<TRequest, TResponse>(() => _client, id);

        // Lưu handlers để xử lý TResponse nhận từ server
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

        _logger.LogInformation("Registered WebSocket client handler for command: {CommandId}", id);
        
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
            throw new InvalidOperationException("WebSocket client is not connected. Call ConnectAsync first.");
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
        _logger.LogInformation("Registered OnConnect handler for WebSocket client");
    }

    public void OnDisconnect(Action handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _onDisconnectHandler = handler;
        _logger.LogInformation("Registered OnDisconnect handler for WebSocket client");
    }

    public void AutoReconnect(bool enable, int maxRetries = 0)
    {
        _logger.LogWarning("🔧 [WS.Register.AutoReconnect] Called with enable={Enable}, maxRetries={MaxRetries}, client={ClientNull}", 
            enable, maxRetries == 0 ? "INFINITE" : maxRetries.ToString(), _client == null ? "NULL" : "NOT NULL");
        
        // Store the settings
        _autoReconnectEnabled = enable;
        _maxReconnectRetries = maxRetries;
        
        if (_client == null)
        {
            _logger.LogWarning("⚠️ [WS.Register.AutoReconnect] Client not initialized yet - settings stored for later application");
            return;
        }

        _logger.LogInformation("✅ [WS.Register.AutoReconnect] Calling _client.EnableAutoReconnect({Enable}, {MaxRetries})", 
            enable, maxRetries == 0 ? "INFINITE" : maxRetries.ToString());
        _client.EnableAutoReconnect(enable, maxRetries);
    }

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new()
    {
        if (_client == null)
            throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");

        return await _client.SendRequestAsync<TRequest, TResponse>(id, request, cancellationToken);
    }

    internal void InvokeOnConnect()
    {
        try
        {
            _onConnectHandler?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnect handler for WebSocket client");
        }
    }

    internal void InvokeOnDisconnect()
    {
        try
        {
            _onDisconnectHandler?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnDisconnect handler for WebSocket client");
        }
    }

    internal bool TryGetHandler(string id, out RegisteredHandler? handler)
    {
        return _handlers.TryGetValue(id, out handler);
    }

    /// <summary>
    /// Dispatch server push message to registered handler
    /// </summary>
    internal void DispatchServerMessage(string commandId, byte[] messageBytes)
    {
        if (!_handlers.TryGetValue(commandId, out var handler))
        {
            _logger.LogWarning("No handler registered for server push message: {CommandId}", commandId);
            return;
        }

        try
        {
            // Get Parser property from ResponseType (protobuf message type)
            var parserProperty = handler.ResponseType.GetProperty("Parser", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            
            if (parserProperty == null)
            {
                _logger.LogError("ResponseType {TypeName} does not have Parser property", handler.ResponseType.Name);
                return;
            }

            var parser = parserProperty.GetValue(null);
            if (parser == null)
            {
                _logger.LogError("Parser is null for {TypeName}", handler.ResponseType.Name);
                return;
            }

            // Get ParseFrom method
            var parseFromMethod = parser.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
            if (parseFromMethod == null)
            {
                _logger.LogError("ParseFrom method not found on parser for {TypeName}", handler.ResponseType.Name);
                return;
            }

            // Parse the message
            var message = parseFromMethod.Invoke(parser, new object[] { messageBytes });
            if (message == null)
            {
                _logger.LogError("Parsed message is null for command: {CommandId}", commandId);
                return;
            }
            
            // Invoke handler with parsed message
            handler.Handler.DynamicInvoke(message);
            _logger.LogDebug("✅ Dispatched server push message: {CommandId}", commandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error dispatching server push message: {CommandId}", commandId);
        }
    }

    internal class RegisteredHandler
    {
        public Type RequestType { get; set; } = null!;
        public Type ResponseType { get; set; } = null!;
        public Delegate Handler { get; set; } = null!;
        public Delegate ErrorHandler { get; set; } = null!;
    }
}
