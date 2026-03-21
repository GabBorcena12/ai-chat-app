using AIChatApp.API.Model;
using AIChatApp.API.Service;
using Microsoft.AspNetCore.Mvc;

namespace AIChatApp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiChatController : ControllerBase
    {
        private readonly ApiChatService _chatService;
        private readonly ILogger<AiChatController> _logger;

        public AiChatController(ILogger<AiChatController> logger, ApiChatService chatService)
        {
            _logger = logger;
            _chatService = chatService;
        }

        [HttpGet("init")]
        public IActionResult Initialize()
        {
            // Can call this when project just start up to extract llama model packages
            return Ok();
        }

        [HttpPost("ask")]
        public async Task<IActionResult> SendChatPrompt([FromBody] ChatRequest request)
        {
            if (request == null 
                || string.IsNullOrWhiteSpace(request.Prompt)
                || string.IsNullOrWhiteSpace(request.User)
                || string.IsNullOrWhiteSpace(request.ChatId))
                return BadRequest("Sorry unable to catch that properly. Please try again later.");

            _logger.LogInformation($"ChatId: {request.ChatId}, User: {request.User}, Question: {request.Prompt}");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _chatService.GetAIResponseForAPI(cts.Token, request);

            stopwatch.Stop();

            _logger.LogInformation($"User: {request.ChatId}, Answer: {response}");
            _logger.LogInformation($"User: {request.ChatId}, Response time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

            return Ok(new ChatResponse
            {
                Prompt = request.Prompt,
                Response = response
            });
        }
    }
}