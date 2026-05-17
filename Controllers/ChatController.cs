using Microsoft.AspNetCore.Mvc;
using SpaApi.Services;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ChatOrchestratorService _orchestrator;
    private readonly ILogger<ChatController> _log;

    public ChatController(ChatOrchestratorService orchestrator, ILogger<ChatController> log)
    {
        _orchestrator = orchestrator;
        _log = log;
    }

    /// <summary>
    /// POST /api/chat
    /// Body: { message, history: [{role, content}], token? }
    /// </summary>
    private const int MaxMessageLength = 4000;

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest req)
    {
        if (req is null)
            return BadRequest(new { answer = "Yêu cầu không hợp lệ." });
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { answer = "Tin nhắn không được để trống." });
        if (req.Message.Length > MaxMessageLength)
            return BadRequest(new { answer = $"Tin nhắn quá dài (tối đa {MaxMessageLength:N0} ký tự)." });

        try
        {
            var result = await _orchestrator.RunAsync(req);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Chat error");

            // User-friendly message based on error type
            var answer = ex.Message.Contains("403") || ex.Message.Contains("PERMISSION_DENIED")
                ? "Dịch vụ AI tạm thời không khả dụng (lỗi xác thực API). Vui lòng liên hệ hotline để được hỗ trợ 📞"
                : ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED")
                ? "Dịch vụ AI đang bận, vui lòng thử lại sau ít phút 🙏"
                : "Xin lỗi, tôi gặp sự cố kỹ thuật. Vui lòng thử lại hoặc liên hệ hotline để được hỗ trợ trực tiếp 📞";

            return Ok(new { answer, toolsUsed = Array.Empty<string>() });
        }
    }
}
