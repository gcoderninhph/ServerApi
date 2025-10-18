

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ServerApi.Abstractions;
using ServerApi.Protos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Asp.net.Services;

public enum TransportType
{
    WebSocket,
    TcpStream
}

public class ResponderInfo
{
    public IResponder<SimpleMessage> Responder { get; set; } = null!;
    public DateTime ConnectedAt { get; set; }
}

public class RegisterServices
{
    // Chia rõ responders theo transport type
    private readonly Dictionary<string, ResponderInfo> _webSocketResponders = new();
    private readonly Dictionary<string, ResponderInfo> _tcpResponders = new();
    private readonly object _respondersLock = new object();
    
    private readonly MessageSnapshotStore _snapshotStore;
    private readonly IHubContext<UiHub> _hubContext;
    private readonly ILogger<RegisterServices> _logger;

    public RegisterServices(
        IServerApiRegistrar registrar,
        MessageSnapshotStore snapshotStore,
        IHubContext<UiHub> hubContext,
        ILogger<RegisterServices> logger)
    {
        _snapshotStore = snapshotStore;
        _hubContext = hubContext;
        _logger = logger;

        // 🎉 Đăng ký handlers với API mới - SẠCH VÀ ĐƠN GIẢN!
        // Cả WebSocket và TCP Stream cùng handler
        registrar.HandleBoth<PingRequest, PingResponse>("ping", OnPingAsync);
        registrar.HandleBoth<SimpleMessage, SimpleMessage>("message.test", OnMessageAsync);
    }

    // 🎉 Handler mới - CLEAN & SIMPLE! Async properly
    private async Task OnPingAsync(IContext context, PingRequest request, IResponder<PingResponse> responder)
    {
        var response = new PingResponse
        {
            Message = $"Pong: {request.Message}",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await responder.SendAsync(response);
    }

    // 🎉 Handler mới - KHÔNG CÒN DirectResponder hack! Responder có thể save và dùng sau!
    private async Task OnMessageAsync(IContext context, SimpleMessage request, IResponder<SimpleMessage> responder)
    {
        var transportType = DetectTransportType(context);

        // Lưu snapshot của request (message nhận được)
        var receivedSnapshot = new MessageSnapshot
        {
            Command = "message.test",
            Content = $"[{transportType}] {request.Message}",
            Direction = MessageDirection.Received,
            Status = MessageStatus.Delivered
        };
        _snapshotStore.AddOrUpdate(receivedSnapshot);
        await _hubContext.Clients.All.SendAsync("SnapshotUpdated", receivedSnapshot);

        // 🎉 Lưu responder vào Dictionary tương ứng với transport type
        lock (_respondersLock)
        {
            var responderInfo = new ResponderInfo
            {
                Responder = responder,
                ConnectedAt = DateTime.UtcNow
            };

            if (transportType == TransportType.WebSocket)
            {
                _webSocketResponders[context.ConnectionId] = responderInfo;
                _logger.LogInformation("📝 Saved WebSocket responder for {ConnectionId} (Total WS: {Count})", 
                    context.ConnectionId, _webSocketResponders.Count);
            }
            else
            {
                _tcpResponders[context.ConnectionId] = responderInfo;
                _logger.LogInformation("📝 Saved TCP responder for {ConnectionId} (Total TCP: {Count})", 
                    context.ConnectionId, _tcpResponders.Count);
            }
        }
        
        // Gửi ACK response
        var ackResponse = new SimpleMessage { Message = $"✅ ACK: {request.Message}" };
        await responder.SendAsync(ackResponse);
    }

    private TransportType DetectTransportType(IContext context)
    {
        // Phân biệt transport type dựa vào TransportType property
        if (context.TransportType == "WebSocket")
        {
            return TransportType.WebSocket;
        }
        return TransportType.TcpStream;
    }

    /// <summary>
    /// Gửi message tới tất cả clients đã kết nối
    /// </summary>
    /// <param name="text">Nội dung message</param>
    /// <param name="transportType">Loại transport (null = gửi tất cả, WebSocket hoặc TcpStream)</param>
    public async Task SendAll(string text, TransportType? transportType = null)
    {
        var message = new SimpleMessage { Message = text };
        int wsCount = 0;
        int tcpCount = 0;
        
        // Xác định Dictionary nào cần gửi
        var shouldSendWebSocket = !transportType.HasValue || transportType.Value == TransportType.WebSocket;
        var shouldSendTcp = !transportType.HasValue || transportType.Value == TransportType.TcpStream;

        // Gửi qua WebSocket
        if (shouldSendWebSocket)
        {
            Dictionary<string, ResponderInfo> wsSnapshot;
            lock (_respondersLock)
            {
                wsSnapshot = new Dictionary<string, ResponderInfo>(_webSocketResponders);
            }

            foreach (var kvp in wsSnapshot)
            {
                try
                {
                    await kvp.Value.Responder.SendAsync(message);
                    wsCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send to WebSocket client {ConnectionId}", kvp.Key);
                }
            }
        }

        // Gửi qua TCP
        if (shouldSendTcp)
        {
            Dictionary<string, ResponderInfo> tcpSnapshot;
            lock (_respondersLock)
            {
                tcpSnapshot = new Dictionary<string, ResponderInfo>(_tcpResponders);
            }

            foreach (var kvp in tcpSnapshot)
            {
                try
                {
                    await kvp.Value.Responder.SendAsync(message);
                    tcpCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send to TCP client {ConnectionId}", kvp.Key);
                }
            }
        }

        var sentCount = wsCount + tcpCount;
        var transportFilter = transportType.HasValue ? transportType.Value.ToString() : "ALL";
        
        _logger.LogInformation("📤 Sent to {Total} clients: WS={WS}, TCP={TCP}, Filter={Filter}", 
            sentCount, wsCount, tcpCount, transportFilter);

        // Lưu snapshot
        var sentSnapshot = new MessageSnapshot
        {
            Command = "message.test",
            Content = $"[{transportFilter}] {text} → WS:{wsCount} TCP:{tcpCount}",
            Direction = MessageDirection.Sent,
            Status = MessageStatus.Sent
        };
        _snapshotStore.AddOrUpdate(sentSnapshot);
        await _hubContext.Clients.All.SendAsync("SnapshotUpdated", sentSnapshot);
    }
}