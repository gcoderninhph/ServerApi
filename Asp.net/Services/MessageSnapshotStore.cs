using System.Collections.Concurrent;

namespace Asp.net.Services;

/// <summary>
/// In-memory store để lưu snapshots của messages
/// Thread-safe với ConcurrentDictionary
/// </summary>
public class MessageSnapshotStore
{
    private readonly ConcurrentDictionary<string, MessageSnapshot> _snapshots = new();
    private readonly ILogger<MessageSnapshotStore> _logger;

    public MessageSnapshotStore(ILogger<MessageSnapshotStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Thêm hoặc cập nhật snapshot
    /// </summary>
    public void AddOrUpdate(MessageSnapshot snapshot)
    {
        _snapshots.AddOrUpdate(snapshot.Id, snapshot, (key, existing) => snapshot);
        _logger.LogInformation("📸 Snapshot updated: {Id} - {Command} - {Status}", 
            snapshot.Id, snapshot.Command, snapshot.Status);
    }

    /// <summary>
    /// Cập nhật status của một message
    /// </summary>
    public void UpdateStatus(string messageId, MessageStatus status, string? errorMessage = null)
    {
        if (_snapshots.TryGetValue(messageId, out var snapshot))
        {
            snapshot.Status = status;
            snapshot.ErrorMessage = errorMessage;
            _logger.LogInformation("📸 Status updated: {Id} -> {Status}", messageId, status);
        }
    }

    /// <summary>
    /// Lấy tất cả snapshots (sắp xếp theo thời gian mới nhất)
    /// </summary>
    public List<MessageSnapshot> GetAll()
    {
        return _snapshots.Values
            .OrderByDescending(s => s.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Lấy snapshot theo ID
    /// </summary>
    public MessageSnapshot? GetById(string id)
    {
        _snapshots.TryGetValue(id, out var snapshot);
        return snapshot;
    }

    /// <summary>
    /// Xóa snapshot
    /// </summary>
    public void Remove(string id)
    {
        if (_snapshots.TryRemove(id, out var snapshot))
        {
            _logger.LogInformation("📸 Snapshot removed: {Id}", id);
        }
    }

    /// <summary>
    /// Xóa tất cả snapshots
    /// </summary>
    public void Clear()
    {
        _snapshots.Clear();
        _logger.LogInformation("📸 All snapshots cleared");
    }

    /// <summary>
    /// Đếm số lượng snapshots
    /// </summary>
    public int Count => _snapshots.Count;
}
