using System;
using kcp2k;
/// using UnityEngine;

namespace ServerApi.Unity.Configs
{
    /// <summary>
    /// TCP Client configuration class.
    /// Contains settings specific to TCP Client operations.
    /// </summary>
    [System.Serializable]
    public class KcpClientConfig : KcpConfig
    {
        ///[Header("General Settings")]
        ///[Tooltip("Process messages on Unity main thread instead of worker threads (slower but allows Unity API calls)")]
        public bool useMainThread = false;

        ///[Header("Connection Settings")]
        ///[Tooltip("TCP server host address")]
        public string host = "localhost";

        ///[Tooltip("TCP server port")]
        public ushort port = 5001;

        ///[Header("Buffer Settings")]
        ///[Tooltip("Size of receive buffer in bytes")]
        public int bufferSize = 8192;

        ///[Tooltip("Maximum message size in bytes (for validation)")]
        public int maxMessageSize = 1048576; // 1MB

        ///[Header("Timeout Settings")]
        ///[Tooltip("Connection timeout in seconds")]
        public int connectionTimeout = 10;

        ///[Tooltip("Request timeout in seconds (for request/response pattern)")]
        public int requestTimeout = 10;

        ///[Header("Multi-Threading Settings")]
        ///[Tooltip("Number of worker threads for message processing (0 = auto-detect based on CPU cores). Ignored if useMainThread is true.")]
        public int workerThreadCount = 0;

        ///[Tooltip("Maximum task queue size for thread pool. Ignored if useMainThread is true.")]
        public int maxQueueSize = 1000;

        ///[Header("Reconnection Settings")]
        ///[Tooltip("Enable automatic reconnection on disconnect")]
        public bool autoReconnect = true;

        ///[Tooltip("Reconnection delay in seconds")]
        public int reconnectDelay = 5;

        ///[Tooltip("Maximum reconnection attempts (0 = infinite)")]
        public int maxReconnectAttempts = 0;

        ///[Header("Logging Settings")]
        ///[Tooltip("Enable verbose logging for debugging")]
        public bool verboseLogging = false;

        ///[Tooltip("Log network traffic (bytes sent/received)")]
        public bool logNetworkTraffic = false;

    }
}
