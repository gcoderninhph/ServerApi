using System;

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
}
