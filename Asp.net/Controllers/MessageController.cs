using Microsoft.AspNetCore.Mvc;
using Asp.net.Services;

namespace Asp.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessageController : ControllerBase
{
    private readonly RegisterServices _registerServices;
    private readonly ILogger<MessageController> _logger;

    public MessageController(RegisterServices registerServices, ILogger<MessageController> logger)
    {
        _registerServices = registerServices;
        _logger = logger;
    }

    /// <summary>
    /// Gửi message tới tất cả clients đã kết nối
    /// </summary>
    [HttpPost("send-all")]
    public async Task<IActionResult> SendAll([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty" });
        }

        try
        {
            await _registerServices.SendAll(request.Message, request.TransportType);
            
            var transportFilter = request.TransportType?.ToString() ?? "ALL";
            _logger.LogInformation("Message sent to {Transport} clients: {Message}", transportFilter, request.Message);
            
            return Ok(new 
            { 
                success = true, 
                message = $"Message sent to {transportFilter} clients",
                transport = transportFilter
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to all clients");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class SendMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public Asp.net.Services.TransportType? TransportType { get; set; }
}
