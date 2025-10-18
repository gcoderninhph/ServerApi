namespace Asp.net.Services;

/// <summary>
/// Snapshot của một message trong hệ thống
/// </summary>
public class MessageSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Command { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MessageDirection Direction { get; set; }
    public MessageStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum MessageDirection
{
    Sent,     // Message gửi đi
    Received  // Message nhận về
}

public enum MessageStatus
{
    Pending,   // Đang chờ gửi
    Sending,   // Đang gửi
    Sent,      // Đã gửi thành công
    Delivered, // Đã nhận được response
    Failed     // Gửi thất bại
}
