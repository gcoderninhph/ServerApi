namespace ServerApi.Configuration;

/// <summary>
/// Configuration cho Security
/// </summary>
public class SecurityConfig
{
    /// <summary>
    /// Bật/tắt tính năng authentication - nếu true, User từ HttpContext sẽ được gán vào Context
    /// </summary>
    public bool EnableAuthentication { get; set; } = false;

    /// <summary>
    /// Bật/tắt yêu cầu user phải authenticated - nếu true, connection sẽ bị reject nếu User == null hoặc không authenticated
    /// </summary>
    public bool RequireAuthenticatedUser { get; set; } = false;
}
