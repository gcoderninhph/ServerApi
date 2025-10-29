using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ClientCore;

/// <summary>
/// Lớp triển khai IRequester để gửi request đến server
/// Sử dụng Func để lấy client động, tránh reference client cũ khi reconnect
/// </summary>
/// <typeparam name="TRequest">Loại request body</typeparam>
/// <typeparam name="TResponse">Loại response body (cần để call client.SendAsync)</typeparam>
internal class Requester<TRequest, TResponse> : IRequester<TRequest>
    where TRequest : class, IMessage
    where TResponse : class, IMessage, new()
{
    private readonly Func<IServerApiClient?> _getClient;  // ✅ Lấy client động
    private readonly string _commandId;

    public Requester(Func<IServerApiClient?> getClient, string commandId)
    {
        _getClient = getClient;
        _commandId = commandId;
    }

    /// <summary>
    /// Gửi request đến server (fire-and-forget - không chờ response)
    /// </summary>
    public async Task SendAsync(TRequest requestBody, CancellationToken cancellationToken = default)
    {
        var client = _getClient();  // ✅ Lấy client hiện tại
        
        if (client == null)
        {
            throw new InvalidOperationException("Client chưa được khởi tạo. Hãy gọi ConnectAsync trước.");
        }

        // Cast to protobuf types
        if (requestBody is not IMessage protoRequest)
        {
            throw new InvalidOperationException($"Request body must implement IMessage (Protocol Buffers)");
        }

        // Gửi request và KHÔNG CHỜ response - response sẽ được handle bởi registered handler
        await client.SendFireAndForgetAsync(_commandId, requestBody, cancellationToken);
    }
}
