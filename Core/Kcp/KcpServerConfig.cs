
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
        public bool UseMainThread = false;

        ///[Header("Server Settings")]
        ///[Tooltip("KCP server listening port")]
        public ushort Port = 5001;

        ///[Header("Multi-Threading Settings")]
        ///[Tooltip("Number of worker threads for message processing (0 = auto-detect based on CPU cores). Ignored if useMainThread is true.")]
        public int WorkerThreadCount = 4;

        ///[Tooltip("Maximum task queue size for thread pool. Ignored if useMainThread is true.")]
        public int MaxQueueSize = 1000;
    }
}
