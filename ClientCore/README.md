# ClientCore - Client Library for ServerApi

Clean and simple client library for connecting to ServerApi via WebSocket or TCP Stream.

## 📁 Project Structure

```
ClientCore/
├── IClientRegister.cs              # Base interface for lifecycle events
├── IRequester.cs                   # Interface for sending requests
├── ClientCoreServiceCollectionExtensions.cs  # DI registration extensions
├── Requester.cs                    # Implementation of IRequester
├── Protos/                         # Protocol Buffers definitions
│   ├── ping.proto
│   ├── message_envelope.proto
│   └── account_info.proto
├── WebSocket/
│   ├── IWebSocketClientRegister.cs # WebSocket client interface
│   ├── WebSocketClientRegister.cs  # WebSocket client implementation
│   └── WebSocketClient.cs          # WebSocket connection handler
└── TcpStream/
    ├── ITcpStreamClientRegister.cs # TCP client interface
    ├── TcpStreamClientRegister.cs  # TCP client implementation
    └── TcpStreamClient.cs          # TCP connection handler
```

## 🚀 Quick Start

### 1. Add Services to DI Container

```csharp
using ClientCore;

var builder = WebApplication.CreateBuilder(args);

// Add WebSocket client support
builder.Services.AddClientApiWebSocket();

// Add TCP Stream client support
builder.Services.AddClientApiTcpStream();

var app = builder.Build();
```

### 2. Use in Your Service

```csharp
using ClientCore;
using ClientCore.WebSocket;
using ClientCore.TcpStream;

public class MyService
{
    private readonly IWebSocketClientRegister _wsRegister;
    private readonly ITcpStreamClientRegister _tcpRegister;

    public MyService(
        IWebSocketClientRegister wsRegister,
        ITcpStreamClientRegister tcpRegister)
    {
        _wsRegister = wsRegister;
        _tcpRegister = tcpRegister;
        
        // Register lifecycle events
        _wsRegister.OnConnect(() => Console.WriteLine("WebSocket Connected!"));
        _wsRegister.OnDisconnect(() => Console.WriteLine("WebSocket Disconnected!"));
        
        _tcpRegister.OnConnect(() => Console.WriteLine("TCP Connected!"));
        _tcpRegister.OnDisconnect(() => Console.WriteLine("TCP Disconnected!"));
    }
}
```

## 📡 WebSocket Usage

### Connect to WebSocket Server

```csharp
// Simple connection
await _wsRegister.ConnectAsync("ws://localhost:5000/ws");

// With headers and query parameters
var headers = new Dictionary<string, string>
{
    ["Authorization"] = "Bearer token123",
    ["X-Custom-Header"] = "value"
};

var queryParams = new Dictionary<string, string>
{
    ["userId"] = "12345",
    ["role"] = "admin"
};

await _wsRegister.ConnectAsync(
    "ws://localhost:5000/ws", 
    headers, 
    queryParams
);
```

### Register Command Handlers

```csharp
// Register handler for PingRequest/PingResponse
var pingRequester = _wsRegister.Register<PingRequest, PingResponse>(
    "ping",
    response => {
        Console.WriteLine($"Received: {response.Message}");
    },
    error => {
        Console.WriteLine($"Error: {error}");
    }
);

// Send ping request
await pingRequester.SendAsync(new PingRequest 
{ 
    Message = "Hello Server!" 
});
```

### Disconnect

```csharp
await _wsRegister.DisconnectAsync();
```

## 🔌 TCP Stream Usage

### Connect to TCP Server

```csharp
await _tcpRegister.ConnectAsync("localhost", 5003);
```

### Register Command Handlers

```csharp
// Register handler for SimpleMessage
var messageRequester = _tcpRegister.Register<SimpleMessage, SimpleMessage>(
    "message.test",
    response => {
        Console.WriteLine($"Received: {response.Message}");
    },
    error => {
        Console.WriteLine($"Error: {error}");
    }
);

// Send message
await messageRequester.SendAsync(new SimpleMessage 
{ 
    Message = "Hello via TCP!" 
});
```

### Disconnect

```csharp
await _tcpRegister.DisconnectAsync();
```

## 🎯 Core Interfaces

### IClientRegister

Base interface providing lifecycle events:

```csharp
public interface IClientRegister
{
    void OnConnect(Action handler);
    void OnDisconnect(Action handler);
}
```

### IRequester<TRequestBody>

Interface for sending requests:

```csharp
public interface IRequester<TRequestBody> where TRequestBody : class
{
    Task SendAsync(
        TRequestBody requestBody, 
        CancellationToken cancellationToken = default
    );
}
```

### IWebSocketClientRegister

WebSocket-specific interface:

```csharp
public interface IWebSocketClientRegister : IClientRegister
{
    IRequester<TRequest> Register<TRequest, TResponse>(
        string id, 
        Action<TResponse> handler, 
        Action<string> errorHandler
    )
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new();

    Task ConnectAsync(
        string url, 
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParameters = null,
        CancellationToken cancellationToken = default
    );

    Task DisconnectAsync();
    
    bool IsConnected { get; }
}
```

### ITcpStreamClientRegister

TCP-specific interface:

```csharp
public interface ITcpStreamClientRegister : IClientRegister
{
    IRequester<TRequest> Register<TRequest, TResponse>(
        string id, 
        Action<TResponse> handler, 
        Action<string> errorHandler
    )
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new();

    Task ConnectAsync(
        string host, 
        int port, 
        CancellationToken cancellationToken = default
    );

    Task DisconnectAsync();
    
    bool IsConnected { get; }
}
```

## 📦 Protocol Buffers

This library uses Protocol Buffers for message serialization. All request/response types must implement `IMessage` from Google.Protobuf.

### Example Proto Definition

```protobuf
syntax = "proto3";

message PingRequest {
  string message = 1;
}

message PingResponse {
  string message = 1;
  int64 timestamp = 2;
}
```

### Generated C# Classes

```csharp
// Auto-generated by protoc
public partial class PingRequest : IMessage<PingRequest>
{
    public string Message { get; set; }
}

public partial class PingResponse : IMessage<PingResponse>
{
    public string Message { get; set; }
    public long Timestamp { get; set; }
}
```

## 🔧 Advanced Usage

### Multiple Command Handlers

```csharp
// Register multiple handlers
var pingRequester = _wsRegister.Register<PingRequest, PingResponse>(
    "ping", OnPingResponse, OnPingError
);

var messageRequester = _wsRegister.Register<SimpleMessage, SimpleMessage>(
    "message.test", OnMessageResponse, OnMessageError
);

var accountRequester = _wsRegister.Register<AccountRequest, AccountResponse>(
    "account.info", OnAccountResponse, OnAccountError
);

// Send requests
await pingRequester.SendAsync(new PingRequest { Message = "Ping!" });
await messageRequester.SendAsync(new SimpleMessage { Message = "Hello!" });
await accountRequester.SendAsync(new AccountRequest { UserId = "123" });
```

### Connection State Management

```csharp
public class ConnectionManager
{
    private readonly IWebSocketClientRegister _wsRegister;
    private bool _isReconnecting;

    public ConnectionManager(IWebSocketClientRegister wsRegister)
    {
        _wsRegister = wsRegister;
        
        _wsRegister.OnConnect(() => {
            _isReconnecting = false;
            Console.WriteLine("✅ Connected!");
        });
        
        _wsRegister.OnDisconnect(async () => {
            if (!_isReconnecting)
            {
                _isReconnecting = true;
                Console.WriteLine("❌ Disconnected! Reconnecting...");
                await Task.Delay(5000); // Wait 5 seconds
                await _wsRegister.ConnectAsync("ws://localhost:5000/ws");
            }
        });
    }
}
```

### Error Handling

```csharp
var requester = _wsRegister.Register<PingRequest, PingResponse>(
    "ping",
    response => {
        // Handle success response
        Console.WriteLine($"✅ Success: {response.Message}");
    },
    error => {
        // Handle error
        Console.WriteLine($"❌ Error: {error}");
        
        // You can implement retry logic here
        // Or show error notification to user
    }
);

try
{
    await requester.SendAsync(new PingRequest { Message = "Test" });
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Send failed: {ex.Message}");
}
```

## 🏗️ Architecture

### Message Flow

1. **Client sends request:**
   ```
   Client App → IRequester.SendAsync() → ClientRegister → WebSocket/TCP → Server
   ```

2. **Server sends response:**
   ```
   Server → WebSocket/TCP → ClientRegister → Handler Action → Client App
   ```

### Thread Safety

- All client registers are **singletons** registered in DI container
- Connection state is managed internally with proper locking
- Handlers are invoked on the message receiving thread
- Use async/await for all I/O operations

### Lifetime Management

- **IWebSocketClientRegister**: Singleton - one instance per application
- **ITcpStreamClientRegister**: Singleton - one instance per application
- **IRequester**: Created per command registration - managed internally

## 🧪 Testing

### Unit Test Example

```csharp
[Fact]
public async Task Should_Send_Ping_Request()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddClientApiWebSocket();
    var provider = services.BuildServiceProvider();
    
    var wsRegister = provider.GetRequiredService<IWebSocketClientRegister>();
    
    PingResponse? received = null;
    var requester = wsRegister.Register<PingRequest, PingResponse>(
        "ping",
        response => received = response,
        error => Assert.Fail($"Error: {error}")
    );
    
    // Act
    await wsRegister.ConnectAsync("ws://localhost:5000/ws");
    await requester.SendAsync(new PingRequest { Message = "Test" });
    
    // Wait for response
    await Task.Delay(1000);
    
    // Assert
    Assert.NotNull(received);
    Assert.Contains("Pong", received.Message);
}
```

## 📊 Performance

- **WebSocket**: Low latency, full-duplex communication
- **TCP Stream**: Length-prefixed messages, efficient binary protocol
- **Protocol Buffers**: Compact binary serialization
- **Connection Pooling**: Single connection per register instance

## 🔒 Security

### WebSocket Security

- Use `wss://` for secure WebSocket connections
- Pass authentication tokens in headers
- Validate server certificates in production

```csharp
var headers = new Dictionary<string, string>
{
    ["Authorization"] = "Bearer YOUR_TOKEN_HERE"
};

await _wsRegister.ConnectAsync(
    "wss://production-server.com/ws",
    headers
);
```

### TCP Security

- Use SSL/TLS wrapper for production
- Implement authentication at application level
- Validate message integrity

## 📝 Best Practices

1. **Always register handlers before connecting**
   ```csharp
   // ✅ Correct
   var requester = register.Register<Req, Res>("cmd", handler, errorHandler);
   await register.ConnectAsync(...);
   
   // ❌ Wrong - might miss early messages
   await register.ConnectAsync(...);
   var requester = register.Register<Req, Res>("cmd", handler, errorHandler);
   ```

2. **Use cancellation tokens for long operations**
   ```csharp
   var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
   await requester.SendAsync(request, cts.Token);
   ```

3. **Handle disconnections gracefully**
   ```csharp
   register.OnDisconnect(async () => {
       await Task.Delay(5000);
       await register.ConnectAsync(...);
   });
   ```

4. **Dispose properly when shutting down**
   ```csharp
   public async Task StopAsync(CancellationToken cancellationToken)
   {
       await _wsRegister.DisconnectAsync();
       await _tcpRegister.DisconnectAsync();
   }
   ```

## 🆚 WebSocket vs TCP Stream

| Feature | WebSocket | TCP Stream |
|---------|-----------|------------|
| Protocol | HTTP Upgrade | Raw TCP |
| Handshake | Yes (HTTP) | No |
| Headers | Supported | No |
| Query Params | Supported | No |
| Browser Support | ✅ Yes | ❌ No |
| Firewall Friendly | ✅ Yes | ⚠️ Maybe |
| Performance | Good | Better |
| Use Case | Web clients, general purpose | High-performance, server-to-server |

## 📚 Related Projects

- **ServerApi.Core**: Server-side library
- **ServerApi.Asp.net**: ASP.NET server implementation
- **Client.Asp.net**: Example client application

---

**Version**: 1.0.0  
**License**: MIT  
**Author**: ServerApi Team
