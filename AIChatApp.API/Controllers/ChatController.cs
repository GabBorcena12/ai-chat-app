using AIChatApp.API.Model;
using AIChatApp.API.Service;
using Microsoft.AspNetCore.Mvc;

namespace AIChatApp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly ApiChatService _chatService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(ILogger<ChatController> logger, ApiChatService chatService)
        {
            _logger = logger;
            _chatService = chatService;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> SendChatPrompt([FromBody] ChatRequest request)
        {
            if (request == null 
                || string.IsNullOrWhiteSpace(request.Prompt)
                || string.IsNullOrWhiteSpace(request.User)
                || string.IsNullOrWhiteSpace(request.ChatId))
                return BadRequest("Sorry unable to catch that properly.");

            _logger.LogInformation($"User: {request.ChatId}, Question: {request.Prompt}");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _chatService.GetAIResponseForAPI(cts.Token, request);

            stopwatch.Stop(); // ADD RESPONSE TIME WHEN SAVING TO DATABASE

            _logger.LogInformation($"User: {request.ChatId}, Answer: {response}");
            _logger.LogInformation($"User: {request.ChatId}, Response time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

            return Ok(new ChatResponse
            {
                Prompt = request.Prompt,
                Response = response
            });
        }

        [HttpGet("history/{chatId}")]
        public async Task<IActionResult> GetChatHistory(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return BadRequest("ChatId is required.");

            var messages = await _chatService.GetChatHistoryAsync(chatId);

            return Ok(messages);
        }
    }
}