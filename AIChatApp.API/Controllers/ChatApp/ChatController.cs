using AIChatApp.API.Models.ChatApp;
using AIChatApp.API.Services.ChatApp.Orchestration;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace AIChatApp.API.Controllers.ChatApp
{
    /// <summary>
    /// Accepts authenticated chat, continuation, and response-reporting requests and translates orchestration output into JSON or SSE responses.
    /// User identity must come from JWT claims before a request reaches the chat workflow.
    /// </summary>
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly ChatOrchestrator _orchestrator;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ChatController> _logger;

        public ChatController(
            ILogger<ChatController> logger,
            ChatOrchestrator orchestrator,
            AppDbContext dbContext)
        {
            _logger = logger;
            _orchestrator = orchestrator;
            _dbContext = dbContext;
        }

        [HttpGet("init")]
        public IActionResult Initialize()
        {
            return Ok();
        }

        [HttpPost("ask-stream")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> AskStream([FromBody] ChatRequest request)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, HttpContext.RequestAborted);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            Response.StatusCode = StatusCodes.Status200OK;
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Append("X-Accel-Buffering", "no");

            try
            {
                ApplyCurrentUser(request);
                await foreach (var chunk in _orchestrator.StreamAsync(request, linkedCts.Token))
                {
                    await WriteServerSentEventAsync(chunk.Type, chunk.Content, linkedCts.Token);
                    await Response.Body.FlushAsync(linkedCts.Token);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
            {
                stopwatch.Stop();

                if (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogInformation("SSE stream canceled by client for chat {chatId} after {seconds:F2} seconds.", request.ChatId, stopwatch.Elapsed.TotalSeconds);
                }
                else
                {
                    _logger.LogWarning("SSE stream timed out for chat {chatId} after {seconds:F2} seconds.", request.ChatId, stopwatch.Elapsed.TotalSeconds);
                }

                return new EmptyResult();
            }

            stopwatch.Stop();
            _logger.LogInformation("User: {chatId}, Stream response time: {seconds:F2} seconds", request.ChatId, stopwatch.Elapsed.TotalSeconds);
            return new EmptyResult();
        }

        [HttpPost("ask-ai")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> AskAi([FromBody] ChatRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.Prompt)
                || string.IsNullOrWhiteSpace(request.User)
                || string.IsNullOrWhiteSpace(request.ChatId))
            {
                return BadRequest("Sorry unable to catch that properly. Please try again later.");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            ApplyCurrentUser(request);
            var response = await _orchestrator.AskAsync(request, cts.Token);

            stopwatch.Stop();
            _logger.LogInformation("User: {chatId}, JSON response time: {seconds:F2} seconds", request.ChatId, stopwatch.Elapsed.TotalSeconds);

            return Ok(new ChatResponse
            {
                Prompt = request.Prompt,
                Response = response
            });
        }

        [HttpPost("ask-continue")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> AskContinue([FromBody] ContinueChatRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.OriginalPrompt)
                || string.IsNullOrWhiteSpace(request.PartialResponse)
                || string.IsNullOrWhiteSpace(request.User)
                || string.IsNullOrWhiteSpace(request.ChatId))
            {
                return BadRequest("Sorry unable to continue that response right now. Please try again.");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            ApplyCurrentUser(request);
            var response = await _orchestrator.ContinueAsync(request, cts.Token);

            stopwatch.Stop();
            _logger.LogInformation("User: {chatId}, Continue response time: {seconds:F2} seconds", request.ChatId, stopwatch.Elapsed.TotalSeconds);

            return Ok(new ChatResponse
            {
                Prompt = request.OriginalPrompt,
                Response = response
            });
        }

        [HttpPost("report-response")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> ReportResponse([FromBody] ReportChatResponseRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.ChatId)
                || string.IsNullOrWhiteSpace(request.MessageId)
                || string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrWhiteSpace(request.AssistantResponse))
            {
                return BadRequest("Unable to save the chat response report.");
            }

            var report = new ChatResponseReportEntity
            {
                ChatId = request.ChatId,
                MessageId = request.MessageId,
                Username = request.Username,
                UserPrompt = request.UserPrompt ?? string.Empty,
                AssistantResponse = request.AssistantResponse,
                ContextMode = request.ContextMode,
                WasUpdated = request.WasUpdated,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ChatResponseReports.Add(report);
            await _dbContext.SaveChangesAsync();

            return Ok("Response report saved.");
        }

        private void ApplyCurrentUser(ChatRequest request)
        {
            var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name) ?? request.User;
            request.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            request.User = string.IsNullOrWhiteSpace(username) ? request.User : username;
        }

        private void ApplyCurrentUser(ContinueChatRequest request)
        {
            var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name) ?? request.User;
            request.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            request.User = string.IsNullOrWhiteSpace(username) ? request.User : username;
        }

        private async Task WriteServerSentEventAsync(string eventName, string content, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new { content });
            await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
            await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        }
    }
}
