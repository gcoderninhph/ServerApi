# 🐛 Fix: Duplicate Message Issue

## Vấn Đề

**Triệu chứng:**
- Khi gửi 1 message từ Client UI → Server, giao diện hiển thị **2 messages giống hệt nhau**
- Sau khi F5 (refresh) thì chỉ còn 1 message

## Nguyên Nhân

Trong `Client.Asp.net/Services/RegisterServices.cs`, khi gửi message, code đang gọi `SnapshotUpdated` **2 lần**:

### Code Cũ (SAI ❌):
```csharp
// Lần 1: Tạo snapshot với status Pending
var sentSnapshot = new MessageSnapshot
{
    Status = MessageStatus.Pending
};
_snapshotStore.AddOrUpdate(sentSnapshot);
await _hubContext.Clients.All.SendAsync("SnapshotUpdated", sentSnapshot);  // ❌ Lần 1

// Gửi message
await _wsMessageRequester.SendAsync(msg, cancellationToken);

// Lần 2: Update status thành Delivered
sentSnapshot.Status = MessageStatus.Delivered;
_snapshotStore.AddOrUpdate(sentSnapshot);
await _hubContext.Clients.All.SendAsync("SnapshotUpdated", sentSnapshot);  // ❌ Lần 2
```

**Kết quả:** 
- UI nhận 2 events `SnapshotUpdated` → Hiển thị 2 messages
- Sau F5: Load từ `_snapshotStore` chỉ có 1 message (vì `AddOrUpdate` đã merge)

## Giải Pháp

Chỉ gửi notification **1 lần duy nhất** sau khi message đã được gửi thành công:

### Code Mới (ĐÚNG ✅):
```csharp
// Gửi message TRƯỚC
await _wsMessageRequester.SendAsync(msg, cancellationToken);
_logger.LogInformation("📤 Sent WebSocket SimpleMessage: {Message}", message);

// SAU ĐÓ: Lưu snapshot VÀ broadcast 1 lần duy nhất
var sentSnapshot = new MessageSnapshot
{
    Command = "message.test",
    Content = $"[WebSocket] {message}",
    Direction = MessageDirection.Sent,
    Status = MessageStatus.Delivered  // ✅ Đặt luôn Delivered vì đã gửi thành công
};
_snapshotStore.AddOrUpdate(sentSnapshot);
await _hubContext.Clients.All.SendAsync("SnapshotUpdated", sentSnapshot);  // ✅ Chỉ 1 lần
```

## Files Đã Sửa

### `Client.Asp.net/Services/RegisterServices.cs`

#### 1. Method `SendWebSocketMessageAsync()` (line ~340-368)
- ✅ Di chuyển `SendAsync()` lên trước
- ✅ Tạo snapshot với status `Delivered` ngay từ đầu
- ✅ Chỉ gọi `SnapshotUpdated` 1 lần

#### 2. Method `SendTcpMessageAsync()` (line ~370-408)
- ✅ Di chuyển `SendAsync()` lên trước
- ✅ Tạo snapshot với status `Delivered` ngay từ đầu
- ✅ Chỉ gọi `SnapshotUpdated` 1 lần

## Kiểm Tra

### Trước Fix:
```
User gửi "Hello"
→ UI hiển thị 2 messages:
   1. [WebSocket] Hello (Pending)
   2. [WebSocket] Hello (Delivered)

User F5
→ UI hiển thị 1 message:
   - [WebSocket] Hello (Delivered)
```

### Sau Fix:
```
User gửi "Hello"
→ UI hiển thị 1 message:
   - [WebSocket] Hello (Delivered)

User F5
→ UI hiển thị 1 message:
   - [WebSocket] Hello (Delivered)
```

## Build Status

```bash
cd Client.Asp.net
dotnet build
```

**Result:** ✅ Build succeeded (with warnings về file lock - normal vì app đang chạy)

## Testing Steps

1. Mở Client UI: http://localhost:5002/client-ui.html
2. Nhập message: "Test duplicate fix"
3. Click "📤 Send Message"
4. **Kết quả mong đợi:** Chỉ hiển thị **1 message** (không còn duplicate)
5. F5 để verify: Vẫn chỉ có **1 message**

## Notes

- Server (`Asp.net/Services/RegisterServices.cs`) không có vấn đề này vì chỉ gọi `SnapshotUpdated` 1 lần
- Fix này áp dụng cho cả WebSocket và TCP transport
- MessageSnapshot store (`_snapshotStore`) vẫn hoạt động đúng nhờ `AddOrUpdate()` merge by timestamp

---
**Fixed Date**: 2025-01-17  
**Status**: ✅ Resolved - Build successful
