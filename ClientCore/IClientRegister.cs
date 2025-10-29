using System;
using Google.Protobuf;

namespace ClientCore;

/// <summary>
/// Interface chung cho client register, cung cấp lifecycle events
/// </summary>
public interface IClientRegister
{
    /// <summary>
    /// Đăng ký handler khi kết nối thành công tới server
    /// </summary>
    void OnConnect(Action handler);

    /// <summary>
    /// Đăng ký handler khi ngắt kết nối từ server
    /// </summary>
    void OnDisconnect(Action handler);

    /// <summary>
    /// Bật/tắt tính năng tự động kết nối lại khi mất kết nối
    /// </summary>
    /// <param name="enable">Bật/tắt auto-reconnect</param>
    /// <param name="maxRetries">Số lần thử lại tối đa (0 = vô hạn, > 0 = giới hạn số lần)</param>
    void AutoReconnect(bool enable, int maxRetries = 0);

    /// <summary>
    /// Gửi một request với việc tương quan requestId và chờ phản hồi với timeout.
    /// Timeout: 20 giây.
    /// </summary>
    Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
    where TRequest : class, IMessage
    where TResponse : class, IMessage, new();
}
