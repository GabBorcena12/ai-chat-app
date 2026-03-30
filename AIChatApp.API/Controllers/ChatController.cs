using AIChatApp.API.Model;
using AIChatApp.API.Services.Generic;
using AIChatApp.API.Services.LLM;
using AIChatApp.API.Services.Orchestration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIChatApp.API.Controllers
{
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly ApiChatService _chatService;
        private readonly ChatOrchestrator _orchestrator;
        private readonly ILLMService _llm;
        private readonly ILogger<ChatController> _logger;

        public ChatController(ILogger<ChatController> logger, 
            ApiChatService chatService, 
            ChatOrchestrator orchestrator,
            ILLMService llm)
        {
            _logger = logger;
            _chatService = chatService;
            _llm = llm;
            _orchestrator = orchestrator;
        }

        [HttpGet("init")]
        public IActionResult Initialize()
        {
            // Can call this when project just start up to extract llama model packages
            return Ok();
        }

        [HttpPost("ask-stream")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> AskStream([FromBody] ChatRequest request)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _orchestrator.AskAsync(request, cts.Token);
            stopwatch.Stop();
            _logger.LogInformation($"User: {request.ChatId}, Response time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            return Ok(new { Prompt = request.Prompt, Response = response });
        }

        #region Comment-out
        //[HttpPost("ask")]
        //public async Task<IActionResult> SendChatPrompt([FromBody] ChatRequest request)
        //{
        //    if (request == null
        //        || string.IsNullOrWhiteSpace(request.Prompt)
        //        || string.IsNullOrWhiteSpace(request.User)
        //        || string.IsNullOrWhiteSpace(request.ChatId))
        //        return BadRequest("Sorry unable to catch that properly. Please try again later.");

        //    _logger.LogInformation($"ChatId: {request.ChatId}, User: {request.User}, Question: {request.Prompt}");

        //    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        //    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        //    var response = await _chatService.GetAIResponseForAPI(cts.Token, request);

        //    stopwatch.Stop();

        //    _logger.LogInformation($"User: {request.ChatId}, Answer: {response}");
        //    _logger.LogInformation($"User: {request.ChatId}, Response time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

        //    return Ok(new ChatResponse
        //    {
        //        Prompt = request.Prompt,
        //        Response = response
        //    });
        //}
        #endregion
    }
}