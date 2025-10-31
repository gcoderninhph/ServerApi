using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Provides methods to send messages to specific connections without receiving responses (Unity version).
    /// Similar to ServerApi.Abstractions.IBroadcastResponder but adapted for Unity.
    /// Used for server-initiated messages (broadcasting).
    /// </summary>
    /// <typeparam name="TBody">The type of message body to send.</typeparam>
    public interface IUnityBroadcastResponder<in TBody> where TBody : class, IMessage
    {
        /// <summary>
        /// Send a message to a specific connection.
        /// </summary>
        /// <param name="connectionId">The connection ID to send to.</param>
        /// <param name="body">The message body to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SendAsync(string connectionId, TBody body, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send an error message to a specific connection.
        /// </summary>
        /// <param name="connectionId">The connection ID to send to.</param>
        /// <param name="errorMessage">The error message text.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SendErrorAsync(string connectionId, string errorMessage, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast a message to all connected clients.
        /// </summary>
        /// <param name="body">The message body to broadcast.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task BroadcastAsync(TBody body, CancellationToken cancellationToken = default);
    }
}
