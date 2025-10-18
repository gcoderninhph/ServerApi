using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ClientCore;

/// <summary>
/// Interface cho việc gửi tin nhắn broadcast mà không cần lắng nghe response.
/// Sử dụng cho trường hợp client muốn gửi tin nhắn one-way đến server.
/// </summary>
/// <typeparam name="TRequest">Loại của đối tượng request, phải là Protobuf message</typeparam>
public interface IBroadcastRequester<in TRequest> where TRequest : class, IMessage
{
    /// <summary>
    /// Gửi request đến server mà không chờ response
    /// </summary>
    /// <param name="request">Đối tượng request cần gửi</param>
    /// <param name="cancellationToken">Token để hủy operation</param>
    /// <returns>Task hoàn thành khi tin nhắn đã được gửi</returns>
    Task SendAsync(TRequest request, CancellationToken cancellationToken = default);
}
