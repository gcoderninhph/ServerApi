using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ClientCore;

/// <summary>
/// Lớp triển khai IRequester để gửi request đến server
/// </summary>
/// <typeparam name="TRequest">Loại request body</typeparam>
/// <typeparam name="TResponse">Loại response body (cần để call client.SendAsync)</typeparam>
internal class Requester<TRequest, TResponse> : IRequester<TRequest>
    where TRequest : class, IMessage
    where TResponse : class, IMessage, new()
{
    private readonly IServerApiClient? _client;
    private readonly string _commandId;

    public Requester(IServerApiClient? client, string commandId)
    {
        _client = client;
        _commandId = commandId;
    }

    /// <summary>
    /// Gửi request đến server
    /// </summary>
    public async Task SendAsync(TRequest requestBody, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client chưa được khởi tạo. Hãy gọi ConnectAsync trước.");
        }

        // Cast to protobuf types
        if (requestBody is not IMessage protoRequest)
        {
            throw new InvalidOperationException($"Request body must implement IMessage (Protocol Buffers)");
        }

        // Gửi request đến server thông qua client, response sẽ được handle bởi registered handler  
        var _ = await _client.SendAsync<TRequest, TResponse>(_commandId, requestBody, cancellationToken);
    }
}
