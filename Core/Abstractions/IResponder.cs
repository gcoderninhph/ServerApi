using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServerApi.Abstractions;

/// <summary>
/// Provides a simple, unified way to send messages back to the client.
/// Can be used immediately in the handler or saved for later use (server push).
/// </summary>
/// <typeparam name="TResponse">The type of message to send.</typeparam>
public interface IResponder<in TResponse> where TResponse : class
{
    /// <summary>
    /// Send a message to the client.
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
