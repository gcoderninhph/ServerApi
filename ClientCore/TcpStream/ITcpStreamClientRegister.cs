

using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ClientCore.TcpStream;
/// <summary>
/// Đăng ký TCP client và các handlers, đây là 1 singleton được đăng ký trong DI container sau khi gọi AddClientApiTcpStream
/// </summary>
public interface ITcpStreamClientRegister : IClientRegister
{
    /// <summary>
    /// Đăng ký một handler cho một command cụ thể
    /// </summary>
    /// <typeparam name="TRequest">
    /// Loại của đối tượng yêu cầu gửi đến server, bắt buộc phải là class được tạo bởi proto buffer
    /// </typeparam>
    /// <typeparam name="TResponse">
    /// Loại của đối tượng phản hồi nhận từ server, bắt buộc phải là class được tạo bởi proto buffer
    /// </typeparam>
    /// <param name="id">
    /// Tên của command để đăng ký handler
    /// </param>
    /// <param name="handler">
    /// Handler để xử lý response nhận được từ server (nhận TResponse)
    /// </param>
    /// <param name="errorHandler">
    /// Handler để xử lý lỗi
    /// </param>
    /// <returns>Requester để gửi request (gửi TRequest)</returns>
    IRequester<TRequest> Register<TRequest, TResponse>(string id, Action<TResponse> handler, Action<string> errorHandler)
        where TRequest : class, IMessage
        where TResponse : class, IMessage, new();

    /// <summary>
    /// Kết nối tới TCP server
    /// </summary>
    /// <param name="host">Host của TCP server (e.g., localhost hoặc 192.168.1.1)</param>
    /// <param name="port">Port của TCP server (e.g., 5001)</param>
    /// <param name="cancellationToken">Token để hủy operation</param>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ngắt kết nối từ TCP server
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Kiểm tra xem client có đang kết nối không
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Tạo một broadcast requester để gửi tin nhắn mà không cần lắng nghe response
    /// </summary>
    /// <typeparam name="TRequest">Loại của đối tượng request, phải là Protobuf message</typeparam>
    /// <param name="commandId">Tên của command</param>
    /// <returns>Broadcast requester để gửi request one-way</returns>
    IBroadcastRequester<TRequest> GetBroadcaster<TRequest>(string commandId)
        where TRequest : class, IMessage;
}
