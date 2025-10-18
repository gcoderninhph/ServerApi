using System;
using System.Collections.Generic;
using ClientCore;
using ClientCore.WebSocket;
using ClientCore.TcpStream;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;

// Alias để tránh conflict giữa ClientCore.PingResponse và ServerApi.Protos.PingResponse
using PingRequest = ClientCore.PingRequest;
using PingResponse = ClientCore.PingResponse;
using SimpleMessage = ServerApi.Protos.SimpleMessage;

namespace Client.Asp.net.Services;

/// <summary>
/// Service đăng ký và xử lý commands cho ClientCore
/// Tương tự RegisterServices trong Asp.net server nhưng dành cho client
/// </summary>
public class RegisterServices
{
    private readonly IWebSocketClientRegister _websocketRegister;
    private readonly ITcpStreamClientRegister _tcpRegister;
    private readonly ILogger<RegisterServices> _logger;
    private readonly MessageSnapshotStore _snapshotStore;
    private readonly IHubContext<UiHub> _hubContext;

    // Requesters để gửi messages
    private IRequester<PingRequest>? _wsPingRequester;
    private IRequester<PingRequest>? _tcpPingRequester;
    private IRequester<SimpleMessage>? _wsMessageRequester;
    private IRequester<SimpleMessage>? _tcpMessageRequester;

    // Properties để truy cập requesters từ bên ngoài
    public IRequester<PingRequest>? WsPingRequester => _wsPingRequester;
    public IRequester<PingRequest>? TcpPingRequester => _tcpPingRequester;
    public IRequester<SimpleMessage>? WsMessageRequester => _wsMessageRequester;
    public IRequester<SimpleMessage>? TcpMessageRequester => _tcpMessageRequester;

    public RegisterServices(
        IWebSocketClientRegister websocketRegister,
        ITcpStreamClientRegister tcpRegister,
        ILogger<RegisterServices> logger,
        MessageSnapshotStore snapshotStore,
        IHubContext<UiHub> hubContext)
    {
        _websocketRegister = websocketRegister;
        _tcpRegister = tcpRegister;
        _logger = logger;
        _snapshotStore = snapshotStore;
        _hubContext = hubContext;

        // Đăng ký lifecycle events
        RegisterLifecycleEvents();
        // Đăng ký Responders
        RegisterRequester();
    }

    private void RegisterLifecycleEvents()
    {
        // WebSocket lifecycle - không có context parameter
        _websocketRegister.OnConnect(OnWebSocketConnect);
        _websocketRegister.OnDisconnect(OnWebSocketDisconnect);

        // TCP lifecycle - không có context parameter
        _tcpRegister.OnConnect(OnTcpConnect);
        _tcpRegister.OnDisconnect(OnTcpDisconnect);

        _logger.LogInformation("Lifecycle events registered");
    }

    private void RegisterRequester()
    {
        // KHÔNG register ở đây nữa - sẽ register trong OnConnect khi client đã sẵn sàng
    }

    // Track transport type cho handlers
    private string _currentTransport = "Unknown";

    #region Handlers
    
    /// <summary>
    /// Handler nhận PingResponse từ server (KHÔNG phải PingRequest!)
    /// </summary>
    private void OnPingResponseHandler(PingResponse response)
    {
        _logger.LogInformation("═══════════════════════════════════════════");
        _logger.LogInformation($"📩 ✅ RECEIVED PingResponse via [{_currentTransport}]!");
        _logger.LogInformation("   Message: {Message}", response.Message);
        _logger.LogInformation("   Timestamp: {Timestamp}", response.Timestamp);
        _logger.LogInformation("═══════════════════════════════════════════");

        // ❌ KHÔNG LƯU snapshot cho ping - không hiển thị lên UI
        // var receivedSnapshot = new MessageSnapshot { ... };
        // _snapshotStore.AddOrUpdate(receivedSnapshot);
        // _hubContext.Clients.All.SendAsync("SnapshotUpdated", receivedSnapshot).Wait();
    }
    
    private void OnPingErrorHandler(string error)
    {
        _logger.LogError("═══════════════════════════════════════════");
        _logger.LogError("❌ PING ERROR!");
        _logger.LogError("   Error: {Error}", error);
        _logger.LogError("═══════════════════════════════════════════");
    }
    
    /// <summary>
    /// Handler nhận SimpleMessage response từ server
    /// </summary>
    private void OnMessageHandler(SimpleMessage message)
    {
        _logger.LogInformation("═══════════════════════════════════════════");
        _logger.LogInformation($"📩 ✅ RECEIVED SimpleMessage via [{_currentTransport}]!");
        _logger.LogInformation("   Message: {Message}", message.Message);
        _logger.LogInformation("═══════════════════════════════════════════");

        // Lưu snapshot của message nhận được với transport info
        var receivedSnapshot = new MessageSnapshot
        {
            Command = "message.test",
            Content = $"[{_currentTransport}] {message.Message}",
            Direction = MessageDirection.Received,
            Status = MessageStatus.Delivered
        };
        _snapshotStore.AddOrUpdate(receivedSnapshot);

        // Broadcast đến Web UI
        _hubContext.Clients.All.SendAsync("SnapshotUpdated", receivedSnapshot).Wait();
    }
    
    private void OnMessageErrorHandler(string error)
    {
        _logger.LogError("═══════════════════════════════════════════");
        _logger.LogError("❌ MESSAGE ERROR!");
        _logger.LogError("   Error: {Error}", error);
        _logger.LogError("═══════════════════════════════════════════");
    }

    #endregion

    #region WebSocket Lifecycle

    private void OnWebSocketConnect()
    {
        _logger.LogInformation("🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐");
        _logger.LogInformation("✅ WebSocket ClientCore CONNECTED!");
        _logger.LogInformation("🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐🌐");

        // Set current transport
        _currentTransport = "WebSocket";

        // Re-register handlers với client đã connected
        _wsPingRequester = _websocketRegister.Register<PingRequest, PingResponse>("ping", OnPingResponseHandler, OnPingErrorHandler);
        _wsMessageRequester = _websocketRegister.Register<SimpleMessage, SimpleMessage>("message.test", OnMessageHandler, OnMessageErrorHandler);
        _logger.LogInformation("📝 WebSocket handlers re-registered");
    }

    private void OnWebSocketDisconnect()
    {
        _logger.LogWarning("🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴");
        _logger.LogWarning("❌ WebSocket ClientCore DISCONNECTED");
        _logger.LogWarning("🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴");

        // Có thể implement auto-reconnect logic
        // await ReconnectWebSocketAsync();
    }

    #endregion

    #region TCP Lifecycle

    private void OnTcpConnect()
    {
        _logger.LogInformation("🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌");
        _logger.LogInformation("✅ TCP ClientCore CONNECTED!");
        _logger.LogInformation("🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌🔌");

        // Set current transport
        _currentTransport = "TCP";

        // Re-register handlers với client đã connected
        _tcpPingRequester = _tcpRegister.Register<PingRequest, PingResponse>("ping", OnPingResponseHandler, OnPingErrorHandler);
        _tcpMessageRequester = _tcpRegister.Register<SimpleMessage, SimpleMessage>("message.test", OnMessageHandler, OnMessageErrorHandler);
        _logger.LogInformation("📝 TCP handlers re-registered");
    }

    private void OnTcpDisconnect()
    {
        _logger.LogWarning("🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴");
        _logger.LogWarning("❌ TCP ClientCore DISCONNECTED");
        _logger.LogWarning("🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴🔴");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Kết nối WebSocket với headers và query parameters
    /// </summary>
    public async Task ConnectWebSocketAsync(
        string url = "ws://localhost:5000/ws",
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Connecting to WebSocket: {Url}", url);
            await _websocketRegister.ConnectAsync(url, headers, queryParams, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect WebSocket");
            throw;
        }
    }

    /// <summary>
    /// Kết nối TCP
    /// </summary>
    public async Task ConnectTcpAsync(
        string host = "localhost",
        int port = 5001,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Connecting to TCP: {Host}:{Port}", host, port);
            await _tcpRegister.ConnectAsync(host, port, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect TCP");
            throw;
        }
    }

    /// <summary>
    /// Ngắt kết nối WebSocket
    /// </summary>
    public async Task DisconnectWebSocketAsync()
    {
        if (_websocketRegister.IsConnected)
        {
            _logger.LogInformation("Disconnecting WebSocket...");
            await _websocketRegister.DisconnectAsync();
        }
    }

    /// <summary>
    /// Ngắt kết nối TCP
    /// </summary>
    public async Task DisconnectTcpAsync()
    {
        if (_tcpRegister.IsConnected)
        {
            _logger.LogInformation("Disconnecting TCP...");
            await _tcpRegister.DisconnectAsync();
        }
    }

    /// <summary>
    /// Kiểm tra trạng thái kết nối
    /// </summary>
    public bool IsWebSocketConnected => _websocketRegister.IsConnected;
    public bool IsTcpConnected => _tcpRegister.IsConnected;

    /// <summary>
    /// Kiểm tra requesters đã sẵn sàng chưa
    /// </summary>
    public bool IsWebSocketReady => _wsPingRequester != null && _wsMessageRequester != null;
    public bool IsTcpReady => _tcpPingRequester != null && _tcpMessageRequester != null;

    #endregion

    #region Send Methods

    /// <summary>
    /// Gửi PingRequest qua WebSocket
    /// </summary>
    public async Task SendWebSocketPingAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_websocketRegister.IsConnected)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        if (_wsPingRequester == null)
        {
            throw new InvalidOperationException("WebSocket Ping requester chưa được khởi tạo. Vui lòng đợi connection hoàn tất.");
        }

        var request = new PingRequest
        {
            Message = message
        };

        await _wsPingRequester.SendAsync(request, cancellationToken);
        _logger.LogInformation("📤 Sent WebSocket PingRequest: {Message}", message);
    }

    /// <summary>
    /// Gửi PingRequest qua TCP
    /// </summary>
    public async Task SendTcpPingAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_tcpRegister.IsConnected)
        {
            throw new InvalidOperationException("TCP is not connected");
        }

        if (_tcpPingRequester == null)
        {
            throw new InvalidOperationException("TCP Ping requester chưa được khởi tạo. Vui lòng đợi connection hoàn tất.");
        }

        var request = new PingRequest
        {
            Message = message
        };

        await _tcpPingRequester.SendAsync(request, cancellationToken);
        _logger.LogInformation("📤 Sent TCP PingRequest: {Message}", message);
    }

    /// <summary>
    /// Gửi SimpleMessage qua WebSocket
    /// </summary>
    public async Task SendWebSocketMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_websocketRegister.IsConnected)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        if (_wsMessageRequester == null)
        {
            throw new InvalidOperationException("WebSocket Message requester chưa được khởi tạo. Vui lòng đợi connection hoàn tất.");
        }

        var msg = new SimpleMessage
        {
            Message = message
        };

        // Gửi message trước
        await _wsMessageRequester.SendAsync(msg, cancellationToken);
        _logger.LogInformation("📤 Sent WebSocket SimpleMessage: {Message}", message);

        // Sau khi gửi thành công, lưu snapshot VÀ broadcast 1 lần duy nhất
        var sentSnapshot = new MessageSnapshot
        {
            Command = "message.test",
            Content = $"[WebSocket] {message}",
            Direction = MessageDirection.Sent,
            Status = MessageStatus.Delivered  // Đặt luôn Delivered vì đã gửi thành công
        };
        _snapshotStore.AddOrUpdate(sentSnapshot);
        await _hubContext.Clients.All.SendAsync("SnapshotUpdated", sentSnapshot);
    }

    /// <summary>
    /// Gửi SimpleMessage qua TCP
    /// </summary>
    public async Task SendTcpMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_tcpRegister.IsConnected)
        {
            throw new InvalidOperationException("TCP is not connected");
        }

        if (_tcpMessageRequester == null)
        {
            throw new InvalidOperationException("TCP Message requester chưa được khởi tạo. Vui lòng đợi connection hoàn tất.");
        }

        var msg = new SimpleMessage
        {
            Message = message
        };

        // Gửi message trước
        await _tcpMessageRequester.SendAsync(msg, cancellationToken);
        _logger.LogInformation("📤 Sent TCP SimpleMessage: {Message}", message);

        // Sau khi gửi thành công, lưu snapshot VÀ broadcast 1 lần duy nhất
        var sentSnapshot = new MessageSnapshot
        {
            Command = "message.test",
            Content = $"[TCP] {message}",
            Direction = MessageDirection.Sent,
            Status = MessageStatus.Delivered  // Đặt luôn Delivered vì đã gửi thành công
        };
        _snapshotStore.AddOrUpdate(sentSnapshot);
        await _hubContext.Clients.All.SendAsync("SnapshotUpdated", sentSnapshot);
    }

    #endregion
}
