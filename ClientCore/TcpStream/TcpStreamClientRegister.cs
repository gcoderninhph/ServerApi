using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace ClientCore.TcpStream;

public class TcpStreamClientRegister : ITcpStreamClientRegister
{
    private readonly ConcurrentDictionary<string, RegisteredHandler> _handlers;
    private readonly ILogger<TcpStreamClientRegister> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Action? _onConnectHandler;
    private Action? _onDisconnectHandler;
    private TcpStreamClient? _client;

    public bool IsConnected => _client?.IsConnected ?? false;
    public TcpStreamClient? Client => _client;

    public TcpStreamClientRegister(ILogger<TcpStreamClientRegister> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _handlers = new ConcurrentDictionary<string, RegisteredHandler>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or whitespace.", nameof(host));
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentException("Port must be between 1 and 65535.", nameof(port));
        }

        var clientLogger = _loggerFactory.CreateLogger<TcpStreamClient>();
        _client = new TcpStreamClient(host, port, clientLogger, this);

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

        // Tạo requester instance để return cho client (dùng để gửi TRequest)
        var requester = new Requester<TRequest, TResponse>(_client!, id);

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

        _logger.LogInformation("Registered TCP Stream client handler for command: {CommandId}", id);
        
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
            throw new InvalidOperationException("TCP Stream client is not connected. Call ConnectAsync first.");
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
        _logger.LogInformation("Registered OnConnect handler for TCP Stream client");
    }

    public void OnDisconnect(Action handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _onDisconnectHandler = handler;
        _logger.LogInformation("Registered OnDisconnect handler for TCP Stream client");
    }

    public void AutoReconnect(bool enable)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot enable auto-reconnect: client is not initialized");
            return;
        }

        _client.EnableAutoReconnect(enable);
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
            _logger.LogError(ex, "Error in OnConnect handler for TCP Stream client");
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
            _logger.LogError(ex, "Error in OnDisconnect handler for TCP Stream client");
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
