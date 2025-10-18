using Microsoft.AspNetCore.Mvc;
using Client.Asp.net.Services;

namespace Client.Asp.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessageController : ControllerBase
{
    private readonly RegisterServices _registerServices;
    private readonly ILogger<MessageController> _logger;

    public MessageController(
        RegisterServices registerServices,
        ILogger<MessageController> logger)
    {
        _registerServices = registerServices;
        _logger = logger;
    }

    /// <summary>
    /// Gửi message qua transport được chọn (WebSocket hoặc TCP)
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message is required" });
        }

        try
        {
            var transportName = request.TransportType == 0 ? "WebSocket" : "TCP";
            _logger.LogInformation($"📤 Sending message via {transportName}");

            if (request.TransportType == 0) // WebSocket
            {
                if (!_registerServices.IsWebSocketConnected)
                {
                    return BadRequest(new { error = "WebSocket is not connected" });
                }
                await _registerServices.SendWebSocketMessageAsync(request.Message);
            }
            else // TCP
            {
                if (!_registerServices.IsTcpConnected)
                {
                    return BadRequest(new { error = "TCP is not connected" });
                }
                await _registerServices.SendTcpMessageAsync(request.Message);
            }

            return Ok(new
            {
                success = true,
                message = $"Message sent via {transportName}",
                transport = transportName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Kiểm tra trạng thái kết nối
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetConnectionStatus()
    {
        return Ok(new
        {
            websocket = new
            {
                connected = _registerServices.IsWebSocketConnected,
                status = _registerServices.IsWebSocketConnected ? "Connected" : "Disconnected"
            },
            tcp = new
            {
                connected = _registerServices.IsTcpConnected,
                status = _registerServices.IsTcpConnected ? "Connected" : "Disconnected"
            }
        });
    }
}

public class SendMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public int TransportType { get; set; } // 0 = WebSocket, 1 = TCP
}
