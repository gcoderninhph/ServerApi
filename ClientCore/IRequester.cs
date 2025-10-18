using System.Threading.Tasks;
using System.Threading;

namespace ClientCore;

/// <summary>
/// Đại diện cho một requester để gửi request qua TCP hoặc WebSocket
/// </summary>
/// <typeparam name="TRequestBody">Loại request body</typeparam>
public interface IRequester<TRequestBody>
    where TRequestBody : class
{
    /// <summary>
    /// Gửi request không đồng bộ
    /// </summary>
    /// <param name="requestBody">Request body để gửi</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task đại diện cho hoạt động gửi request</returns>
    Task SendAsync(TRequestBody requestBody, CancellationToken cancellationToken = default);
}