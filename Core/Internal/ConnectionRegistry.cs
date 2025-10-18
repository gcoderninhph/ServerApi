using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ServerApi.Internal;

/// <summary>
/// Manages active connections for WebSocket and TCP Stream.
/// </summary>
internal class ConnectionRegistry : IConnectionRegistry
{
    private readonly ConcurrentDictionary<string, Func<byte[], Task>> _webSocketConnections = new();
    private readonly ConcurrentDictionary<string, Func<byte[], Task>> _tcpStreamConnections = new();

    public void RegisterWebSocket(string connectionId, Func<byte[], Task> sendFunc)
    {
        _webSocketConnections[connectionId] = sendFunc;
    }

    public void RegisterTcpStream(string connectionId, Func<byte[], Task> sendFunc)
    {
        _tcpStreamConnections[connectionId] = sendFunc;
    }

    public void UnregisterWebSocket(string connectionId)
    {
        _webSocketConnections.TryRemove(connectionId, out _);
    }

    public void UnregisterTcpStream(string connectionId)
    {
        _tcpStreamConnections.TryRemove(connectionId, out _);
    }

    public async Task<bool> TrySendWebSocketAsync(string connectionId, byte[] data, CancellationToken cancellationToken = default)
    {
        if (_webSocketConnections.TryGetValue(connectionId, out var sendFunc))
        {
            await sendFunc(data);
            return true;
        }
        return false;
    }

    public async Task<bool> TrySendTcpStreamAsync(string connectionId, byte[] data, CancellationToken cancellationToken = default)
    {
        if (_tcpStreamConnections.TryGetValue(connectionId, out var sendFunc))
        {
            await sendFunc(data);
            return true;
        }
        return false;
    }
}
