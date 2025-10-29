using System;
using System.Collections.Generic;

namespace ServerApi.Configuration;

/// <summary>
/// Root configuration options for ServerApi.
/// </summary>
public class ServerApiOptions
{
    public const string SectionName = "ServerApi";

    /// <summary>
    /// Security configuration options.
    /// </summary>
    public SecurityConfig? Security { get; set; }

    /// <summary>
    /// WebSocket configuration options.
    /// </summary>
    public WebSocketOptions? WebSocket { get; set; }

    /// <summary>
    /// TCP Stream configuration options.
    /// </summary>
    public TcpStreamOptions? TcpStream { get; set; }

    /// <summary>
    /// KCP configuration options.
    /// </summary>
    public KcpOptions? Kcp { get; set; }
}

/// <summary>
/// Configuration options for WebSocket transport.
/// </summary>
public class WebSocketOptions
{
    /// <summary>
    /// List of path patterns that WebSocket gateway will handle.
    /// Example: ["/ws", "/api/websocket"]
    /// </summary>
    public List<string> Patterns { get; set; } = new() { "/ws" };

    /// <summary>
    /// Buffer size for WebSocket operations in bytes.
    /// </summary>
    public int BufferSize { get; set; } = 4096;

    /// <summary>
    /// Keep-alive interval in seconds. Set to 0 to disable.
    /// </summary>
    public int KeepAliveInterval { get; set; } = 30;
}

/// <summary>
/// Configuration options for TCP Stream transport.
/// </summary>
public class TcpStreamOptions
{
    /// <summary>
    /// TCP port to listen on.
    /// </summary>
    public int Port { get; set; } = 5003;

    /// <summary>
    /// Buffer size for TCP Stream operations in bytes.
    /// </summary>
    public int BufferSize { get; set; } = 8192;

    /// <summary>
    /// Maximum number of concurrent connections. Set to 0 for unlimited.
    /// </summary>
    public int MaxConnections { get; set; } = 1000;
}

/// <summary>
/// Configuration options for KCP transport.
/// KCP is a UDP-based reliable protocol with better performance than TCP in some scenarios.
/// </summary>
public class KcpOptions
{
    /// <summary>
    /// KCP port to listen on.
    /// </summary>
    public int Port { get; set; } = 5004;
}
