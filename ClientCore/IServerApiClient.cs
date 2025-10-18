using Google.Protobuf;
using System.Threading;
using System.Threading.Tasks;

namespace ClientCore;

internal interface IServerApiClient
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<TResponse> SendAsync<TRequest, TResponse>(string commandName, TRequest request, CancellationToken cancellationToken = default) where TRequest : IMessage where TResponse : IMessage, new();
    bool IsConnected { get; }
}
