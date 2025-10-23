using Google.Protobuf;
using System.Threading;
using System.Threading.Tasks;

namespace ClientCore;

internal interface IServerApiClient
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    
    /// <summary>
    /// Send a request with requestId correlation and wait for response with timeout.
    /// Timeout: 20 seconds.
    /// </summary>
    Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default) 
        where TRequest : class, IMessage 
        where TResponse : class, IMessage, new();
    
    /// <summary>
    /// Send a fire-and-forget request without waiting for response. Response will be handled by registered handler.
    /// </summary>
    Task SendFireAndForgetAsync<TRequest>(string commandName, TRequest request, CancellationToken cancellationToken = default) 
        where TRequest : IMessage;
    
    bool IsConnected { get; }
}
