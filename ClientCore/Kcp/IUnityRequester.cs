using System.Threading;
using System.Threading.Tasks;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Represents a requester to send requests via TCP or WebSocket (Unity version).
    /// Similar to ClientCore.IRequester but adapted for Unity.
    /// </summary>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    public interface IUnityRequester<in TRequest> where TRequest : class
    {
        /// <summary>
        /// Send a request asynchronously and wait for response.
        /// </summary>
        /// <param name="requestBody">The request body to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the send operation.</returns>
        Task SendAsync(TRequest requestBody, CancellationToken cancellationToken = default);
    }
}
