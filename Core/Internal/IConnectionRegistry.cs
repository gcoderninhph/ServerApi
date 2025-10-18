using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServerApi.Internal;

/// <summary>
/// Internal interface for managing active connections.
/// </summary>
internal interface IConnectionRegistry
{
    /// <summary>
    /// Register a WebSocket connection with its send function.
    /// </summary>
    void RegisterWebSocket(string connectionId, Func<byte[], Task> sendFunc);

    /// <summary>
    /// Register a TCP Stream connection with its send function.
    /// </summary>
    void RegisterTcpStream(string connectionId, Func<byte[], Task> sendFunc);

    /// <summary>
    /// Unregister a WebSocket connection.
    /// </summary>
    void UnregisterWebSocket(string connectionId);

    /// <summary>
    /// Unregister a TCP Stream connection.
    /// </summary>
    void UnregisterTcpStream(string connectionId);

    /// <summary>
    /// Try to send data to a WebSocket connection.
    /// </summary>
    Task<bool> TrySendWebSocketAsync(string connectionId, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Try to send data to a TCP Stream connection.
    /// </summary>
    Task<bool> TrySendTcpStreamAsync(string connectionId, byte[] data, CancellationToken cancellationToken = default);
}
