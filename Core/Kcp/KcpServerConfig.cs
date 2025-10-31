
using kcp2k;
///using UnityEngine;

namespace ServerApi.Unity.Configs
{
    /// <summary>
    /// TCP Server configuration class.
    /// Contains settings specific to TCP Server operations.
    /// </summary>
    [System.Serializable]
    public class KcpServerConfig : KcpConfig
    {
        ///[Header("General Settings")]
        ///[Tooltip("Process messages on Unity main thread instead of worker threads (slower but allows Unity API calls)")]
        public bool useMainThread = false;

        ///[Header("Server Settings")]
        ///[Tooltip("KCP server listening port")]
        public ushort port = 5001;

        ///[Header("Multi-Threading Settings")]
        ///[Tooltip("Number of worker threads for message processing (0 = auto-detect based on CPU cores). Ignored if useMainThread is true.")]
        public int workerThreadCount = 0;

        ///[Tooltip("Maximum task queue size for thread pool. Ignored if useMainThread is true.")]
        public int maxQueueSize = 1000;

        ///[Header("Logging Settings")]
        ///[Tooltip("Enable verbose logging for debugging")]
        public bool verboseLogging = false;

        ///[Tooltip("Log network traffic (bytes sent/received)")]
        public bool logNetworkTraffic = false;
    }
}
