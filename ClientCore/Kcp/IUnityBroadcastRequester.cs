using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Interface for sending broadcast messages without listening for responses (Unity version).
    /// Similar to ClientCore.IBroadcastRequester but adapted for Unity.
    /// Used for client-initiated one-way messages to server.
    /// </summary>
    /// <typeparam name="TRequest">The request object type, must be a Protobuf message.</typeparam>
    public interface IUnityBroadcastRequester<in TRequest> where TRequest : class, IMessage
    {
        /// <summary>
        /// Send a request to server without waiting for response.
        /// </summary>
        /// <param name="request">The request object to send.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Task that completes when the message has been sent.</returns>
        Task SendAsync(TRequest request, CancellationToken cancellationToken = default);
    }
}
