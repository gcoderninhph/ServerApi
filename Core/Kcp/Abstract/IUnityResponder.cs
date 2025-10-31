using System.Threading;
using System.Threading.Tasks;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Provides a unified way to send messages back to the client (Unity version).
    /// Similar to ServerApi.Abstractions.IResponder but adapted for Unity.
    /// Can be used immediately in the handler or saved for later use (server push).
    /// </summary>
    /// <typeparam name="TResponse">The type of message to send.</typeparam>
    public interface IUnityResponder<in TResponse> where TResponse : class
    {
        /// <summary>
        /// Send a response message to the client.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SendAsync(TResponse message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send an error message to the client.
        /// </summary>
        /// <param name="errorMessage">The error message text.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SendErrorAsync(string errorMessage, CancellationToken cancellationToken = default);
    }
}
