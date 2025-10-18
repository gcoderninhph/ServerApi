using Asp.net.Services;
using Microsoft.AspNetCore.SignalR;

namespace Asp.net.Services;

/// <summary>
/// SignalR Hub cho Web UI để theo dõi trạng thái messages
/// </summary>
public class UiHub : Hub
{
    private readonly MessageSnapshotStore _snapshotStore;
    private readonly ILogger<UiHub> _logger;

    public UiHub(MessageSnapshotStore snapshotStore, ILogger<UiHub> logger)
    {
        _snapshotStore = snapshotStore;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("🌐 Web UI connected: {ConnectionId}", Context.ConnectionId);
        
        // Gửi tất cả snapshots hiện tại cho client mới kết nối
        var snapshots = _snapshotStore.GetAll();
        await Clients.Caller.SendAsync("InitialSnapshots", snapshots);
        
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("🌐 Web UI disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Web UI gọi để lấy tất cả snapshots
    /// </summary>
    public async Task GetAllSnapshots()
    {
        var snapshots = _snapshotStore.GetAll();
        await Clients.Caller.SendAsync("SnapshotList", snapshots);
    }

    /// <summary>
    /// Web UI gọi để xóa tất cả snapshots
    /// </summary>
    public async Task ClearSnapshots()
    {
        _snapshotStore.Clear();
        await Clients.All.SendAsync("SnapshotsCleared");
    }
}
