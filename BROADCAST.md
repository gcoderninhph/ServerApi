# Broadcast Responder & Requester

## Core - Server Side Broadcast

### Tổng quan
Server có thể gửi tin nhắn đến các kết nối cụ thể mà không cần chờ phản hồi thông qua `IBroadcastResponder`.

### Interface

```csharp
public interface IBroadcastResponder<in TBody> where TBody : class, IMessage
{
    Task SendAsync(string connectionId, TBody body, CancellationToken cancellationToken = default);
    Task SendErrorAsync(string connectionId, string errorMessage, CancellationToken cancellationToken = default);
}
```

### Cách sử dụng

#### 1. Lấy Broadcaster từ IServerApiRegistrar

```csharp
public class NotificationService
{
    private readonly IServerApiRegistrar _registrar;
    
    public NotificationService(IServerApiRegistrar registrar)
    {
        _registrar = registrar;
    }
    
    public async Task SendNotificationToUser(string connectionId, string message)
    {
        // WebSocket Broadcaster
        var wsBroadcaster = _registrar.GetWebSocketBroadcaster<NotificationMessage>("notify");
        await wsBroadcaster.SendAsync(connectionId, new NotificationMessage 
        { 
            Message = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        
        // TCP Stream Broadcaster
        var tcpBroadcaster = _registrar.GetTcpStreamBroadcaster<NotificationMessage>("notify");
        await tcpBroadcaster.SendAsync(connectionId, new NotificationMessage 
        { 
            Message = message 
        });
    }
    
    public async Task SendErrorToUser(string connectionId, string errorMessage)
    {
        var broadcaster = _registrar.GetWebSocketBroadcaster<NotificationMessage>("notify");
        await broadcaster.SendErrorAsync(connectionId, errorMessage);
    }
}
```

#### 2. Ví dụ: Push thông báo real-time

```csharp
// Trong một background service hoặc event handler
public class OrderEventHandler
{
    private readonly IServerApiRegistrar _registrar;
    
    public async Task OnOrderStatusChanged(string userId, Order order)
    {
        // Giả sử có mapping từ userId -> connectionId
        var connectionId = GetConnectionIdForUser(userId);
        
        var broadcaster = _registrar.GetWebSocketBroadcaster<OrderStatusUpdate>("order.status");
        await broadcaster.SendAsync(connectionId, new OrderStatusUpdate
        {
            OrderId = order.Id,
            Status = order.Status,
            UpdatedAt = order.UpdatedAt
        });
    }
}
```

### Kiến trúc

```
IServerApiRegistrar.GetWebSocketBroadcaster<TBody>("commandId")
    ↓
BroadcastResponder<TBody>
    ↓
MessageEnvelopeFactory.CreateResponse(commandId, body)
    ↓
ConnectionRegistry.TrySendWebSocketAsync(connectionId, bytes)
    ↓
WebSocketConnection (send via socket)
```

### Lưu ý

1. **Connection Management**: Connection tự động đăng ký/hủy đăng ký khi connect/disconnect
2. **Thread-safe**: `ConnectionRegistry` sử dụng `ConcurrentDictionary` để đảm bảo thread-safe
3. **Error Handling**: Nếu connection không tồn tại, `SendAsync` sẽ throw `InvalidOperationException`
4. **Transport Separation**: WebSocket và TCP Stream có các registry riêng biệt

---

## ClientCore - Client Side Broadcast

### Tổng quan
Client có thể gửi tin nhắn one-way đến server mà không cần chờ phản hồi thông qua `IBroadcastRequester`.

### Interface

```csharp
public interface IBroadcastRequester<in TRequest> where TRequest : class, IMessage
{
    Task SendAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

### Cách sử dụng

#### 1. WebSocket Client Broadcast

```csharp
public class MyClientService
{
    private readonly IWebSocketClientRegister _wsRegister;
    
    public MyClientService(IWebSocketClientRegister wsRegister)
    {
        _wsRegister = wsRegister;
    }
    
    public async Task SendHeartbeat()
    {
        // Tạo broadcaster cho command "heartbeat"
        var broadcaster = _wsRegister.GetBroadcaster<HeartbeatRequest>("heartbeat");
        
        // Gửi tin nhắn mà không chờ response
        await broadcaster.SendAsync(new HeartbeatRequest
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
    
    public async Task LogActivity(string activity)
    {
        var broadcaster = _wsRegister.GetBroadcaster<ActivityLog>("activity.log");
        
        await broadcaster.SendAsync(new ActivityLog
        {
            Activity = activity,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
}
```

#### 2. TCP Stream Client Broadcast

```csharp
public class TcpClientService
{
    private readonly ITcpStreamClientRegister _tcpRegister;
    
    public TcpClientService(ITcpStreamClientRegister tcpRegister)
    {
        _tcpRegister = tcpRegister;
    }
    
    public async Task SendTelemetry(double cpuUsage, double memoryUsage)
    {
        var broadcaster = _tcpRegister.GetBroadcaster<TelemetryData>("telemetry");
        
        await broadcaster.SendAsync(new TelemetryData
        {
            CpuUsage = cpuUsage,
            MemoryUsage = memoryUsage,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
}
```

#### 3. So sánh với Request-Response thông thường

```csharp
// ❌ Request-Response (chờ response)
var requester = _wsRegister.Register<PingRequest, PingResponse>(
    "ping",
    response => Console.WriteLine($"Pong: {response.Message}"),
    error => Console.WriteLine($"Error: {error}")
);
await requester.SendAsync(new PingRequest { Message = "Hello" });

// ✅ Broadcast (không chờ response)
var broadcaster = _wsRegister.GetBroadcaster<PingRequest>("ping");
await broadcaster.SendAsync(new PingRequest { Message = "Hello" });
```

### Kiến trúc

```
IWebSocketClientRegister.GetBroadcaster<TRequest>("commandId")
    ↓
BroadcastRequester<TRequest>
    ↓
request.ToByteArray()
    ↓
WebSocketClient.SendBroadcastAsync(commandId, bytes)
    ↓
MessageEnvelope (Id=commandId, Type=Request, Data=bytes)
    ↓
WebSocket.SendAsync()
```

### Khi nào sử dụng Broadcast?

#### ✅ Nên dùng Broadcast khi:
- Gửi telemetry/metrics định kỳ
- Gửi heartbeat/ping để duy trì kết nối
- Gửi activity logs
- Fire-and-forget messages
- Không quan tâm đến kết quả từ server

#### ❌ Không nên dùng Broadcast khi:
- Cần xác nhận từ server (acknowledgment)
- Cần nhận dữ liệu response
- Cần xử lý lỗi cụ thể từ server
- Transaction/business logic quan trọng

### Lưu ý

1. **No Response**: Không có cách nào để nhận response từ server
2. **Connection Required**: Phải gọi `ConnectAsync()` trước khi sử dụng `GetBroadcaster()`
3. **Exception Handling**: Chỉ throw exception khi gửi thất bại (network error), không có error callback
4. **Performance**: Broadcast nhanh hơn request-response vì không cần chờ và không cần quản lý `TaskCompletionSource`

---

## So sánh Core vs ClientCore

| Khía cạnh | Core (Server) | ClientCore (Client) |
|-----------|---------------|---------------------|
| Interface | `IBroadcastResponder<TBody>` | `IBroadcastRequester<TRequest>` |
| Method | `SendAsync(connectionId, body)` | `SendAsync(request)` |
| Mục đích | Server push đến client cụ thể | Client gửi one-way đến server |
| Cần connectionId? | ✅ Có (server cần biết gửi cho ai) | ❌ Không (luôn gửi đến server đã kết nối) |
| Factory Method | `GetWebSocketBroadcaster<TBody>(commandId)` | `GetBroadcaster<TRequest>(commandId)` |
| Use Case | Notifications, Server Push | Telemetry, Heartbeat, Logs |

---

## Ví dụ End-to-End

### Scenario: Server push notification đến client

#### Server Code (Asp.net)

```csharp
public class NotificationController : ControllerBase
{
    private readonly IServerApiRegistrar _registrar;
    
    [HttpPost("send-notification")]
    public async Task<IActionResult> SendNotification(
        [FromBody] SendNotificationRequest request)
    {
        // Server broadcast đến client
        var broadcaster = _registrar.GetWebSocketBroadcaster<NotificationMessage>("notification");
        await broadcaster.SendAsync(request.ConnectionId, new NotificationMessage
        {
            Title = request.Title,
            Message = request.Message
        });
        
        return Ok("Notification sent");
    }
}
```

#### Client Code (Client.Asp.net)

```csharp
public class ClientNotificationService
{
    private readonly IWebSocketClientRegister _register;
    
    public async Task Initialize()
    {
        // Client đăng ký handler để nhận notification từ server
        _register.Register<EmptyRequest, NotificationMessage>(
            "notification",
            notification => 
            {
                Console.WriteLine($"📬 Received: {notification.Title} - {notification.Message}");
            },
            error => Console.WriteLine($"❌ Error: {error}")
        );
        
        await _register.ConnectAsync("ws://localhost:5000/ws");
    }
}
```

### Scenario: Client gửi heartbeat đến server

#### Client Code

```csharp
public class HeartbeatService : BackgroundService
{
    private readonly IWebSocketClientRegister _register;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var broadcaster = _register.GetBroadcaster<HeartbeatRequest>("heartbeat");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            await broadcaster.SendAsync(new HeartbeatRequest
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

#### Server Code

```csharp
public class HeartbeatCommandRegistrar : IServerApiCommandRegistrar
{
    public void Register(IServerApiRegistrar registrar)
    {
        // Server nhận heartbeat từ client
        registrar.HandleWebSocket<HeartbeatRequest, EmptyResponse>(
            "heartbeat",
            async (request, context) =>
            {
                _logger.LogInformation(
                    "💓 Heartbeat from {ConnectionId} at {Timestamp}",
                    context.ConnectionId,
                    DateTimeOffset.FromUnixTimeSeconds(request.Timestamp)
                );
                
                return new EmptyResponse();
            }
        );
    }
}
```
