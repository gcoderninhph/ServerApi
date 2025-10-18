# Network Configuration - Listening on All Interfaces (0.0.0.0)

## Vấn đề
Server ban đầu chỉ lắng nghe trên `localhost` (127.0.0.1), nên các máy khác trong mạng không thể kết nối được.

## Giải pháp

### 1. Cấu hình Kestrel (Program.cs)

**Trước:**
```csharp
builder.WebHost.UseUrls("http://localhost:5000", "http://localhost:5001");
```

**Sau:**
```csharp
builder.WebHost.UseUrls("http://0.0.0.0:5000", "http://0.0.0.0:5001");
```

### 2. Cấu hình CORS (Program.cs)

**Trước:**
```csharp
policy.WithOrigins("http://localhost:5001", "http://localhost:5002")
      .AllowAnyMethod()
      .AllowAnyHeader()
      .AllowCredentials();
```

**Sau:**
```csharp
policy.AllowAnyOrigin()  // Cho phép mọi nguồn
      .AllowAnyMethod()
      .AllowAnyHeader();
```

> ⚠️ **Lưu ý**: `AllowAnyOrigin()` không thể dùng cùng với `AllowCredentials()`. Nếu cần credentials, phải chỉ định cụ thể origins.

### 3. TCP Stream Gateway

TCP Stream đã tự động lắng nghe trên `0.0.0.0`:
```csharp
// Core/TcpStream/Gateway.cs
_listener = new TcpListener(IPAddress.Any, _options.Port);
```

## Kết quả

Server bây giờ lắng nghe trên tất cả các network interfaces:

```
=== Asp.net Server (Listening on 0.0.0.0) ===
WebSocket: ws://0.0.0.0:5000/ws (or ws://192.168.1.100:5000/ws)
HTTP API:  http://0.0.0.0:5001 (or http://192.168.1.100:5001)
TCP Stream: tcp://0.0.0.0:5003 (or tcp://192.168.1.100:5003)
Web UI:    http://192.168.1.100:5001/server-ui.html
```

## Cách kết nối từ máy khác

### Tìm địa chỉ IP của server

**Windows:**
```powershell
ipconfig
# Tìm "IPv4 Address" trong phần Ethernet adapter hoặc Wi-Fi adapter
```

**Linux/macOS:**
```bash
ip addr show
# hoặc
ifconfig
```

### Kết nối từ client

Giả sử IP của server là `192.168.1.100`:

**WebSocket:**
```csharp
await _register.ConnectAsync("ws://192.168.1.100:5000/ws");
```

**TCP Stream:**
```csharp
await _register.ConnectAsync("192.168.1.100", 5003);
```

**HTTP API:**
```bash
curl http://192.168.1.100:5001/api/message
```

**Web UI:**
```
http://192.168.1.100:5001/server-ui.html
```

## Bảo mật

### Development (hiện tại)
- Lắng nghe: `0.0.0.0` (tất cả interfaces)
- CORS: `AllowAnyOrigin()` (cho phép mọi nguồn)
- Authentication: Disabled

✅ Phù hợp cho: Development, testing, mạng nội bộ tin cậy

### Production (khuyến nghị)

#### 1. Sử dụng HTTPS
```csharp
builder.WebHost.UseUrls("https://0.0.0.0:5000", "https://0.0.0.0:5001");
```

#### 2. Giới hạn CORS
```csharp
policy.WithOrigins(
    "https://yourdomain.com",
    "https://app.yourdomain.com"
)
.AllowAnyMethod()
.AllowAnyHeader()
.AllowCredentials();
```

#### 3. Bật Authentication
```json
// appsettings.json
{
  "ServerApi": {
    "Security": {
      "EnableAuthentication": true,
      "RequireAuthenticatedUser": true
    }
  }
}
```

#### 4. Firewall Rules
```bash
# Chỉ cho phép IP cụ thể kết nối
# Windows Firewall
New-NetFirewallRule -DisplayName "ServerApi" -Direction Inbound -LocalPort 5000,5001,5003 -Protocol TCP -Action Allow -RemoteAddress 192.168.1.0/24

# Linux iptables
iptables -A INPUT -p tcp --dport 5000:5003 -s 192.168.1.0/24 -j ACCEPT
iptables -A INPUT -p tcp --dport 5000:5003 -j DROP
```

#### 5. Rate Limiting
```csharp
// Thêm rate limiting middleware
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

## Troubleshooting

### Vẫn không kết nối được?

1. **Kiểm tra Firewall**
   ```powershell
   # Windows - Tắt tạm firewall để test
   netsh advfirewall set allprofiles state off
   
   # Bật lại sau khi test
   netsh advfirewall set allprofiles state on
   ```

2. **Kiểm tra port đã được lắng nghe chưa**
   ```powershell
   # Windows
   netstat -ano | findstr :5000
   netstat -ano | findstr :5001
   netstat -ano | findstr :5003
   
   # Linux
   netstat -tuln | grep 5000
   ```

3. **Kiểm tra antivirus**
   - Một số antivirus chặn incoming connections
   - Thêm exception cho ứng dụng

4. **Kiểm tra network**
   ```bash
   # Ping server từ client
   ping 192.168.1.100
   
   # Test port connectivity
   telnet 192.168.1.100 5000
   ```

5. **Kiểm tra log**
   - Xem file log trong thư mục `Logs/`
   - Kiểm tra console output khi server start

## Testing

### Test từ localhost
```bash
# WebSocket
wscat -c ws://localhost:5000/ws

# HTTP
curl http://localhost:5001/api/message

# TCP (PowerShell)
Test-NetConnection -ComputerName localhost -Port 5003
```

### Test từ máy khác trong mạng
```bash
# WebSocket
wscat -c ws://192.168.1.100:5000/ws

# HTTP
curl http://192.168.1.100:5001/api/message

# TCP (PowerShell)
Test-NetConnection -ComputerName 192.168.1.100 -Port 5003
```

## Tóm tắt thay đổi

| Khía cạnh | Trước | Sau |
|-----------|-------|-----|
| WebSocket | `localhost:5000` | `0.0.0.0:5000` |
| HTTP API | `localhost:5001` | `0.0.0.0:5001` |
| TCP Stream | `0.0.0.0:5003` (đã có) | `0.0.0.0:5003` (không đổi) |
| CORS | Chỉ localhost | `AllowAnyOrigin()` |
| Kết nối từ mạng | ❌ Không | ✅ Có |
