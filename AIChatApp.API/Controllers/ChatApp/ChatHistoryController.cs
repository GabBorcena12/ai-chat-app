using AIChatApp.API.Models.ChatApp;
using AIChatApp.API.Services.ChatApp.History;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AIChatApp.API.Controllers.ChatApp
{
    /// <summary>
    /// Exposes user-scoped conversation history, title updates, and soft deletion.
    /// Always pass the authenticated NameIdentifier to the history service so one user cannot access another user's conversations.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ChatHistoryController : ControllerBase
    {
        private readonly ChatHistoryService _chatService;
        private readonly ILogger<ChatHistoryController> _logger;

        public ChatHistoryController(ILogger<ChatHistoryController> logger, ChatHistoryService chatService)
        {
            _logger = logger;
            _chatService = chatService;
        }

        [HttpGet("conversations/{chatId}")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> GetConversation(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return BadRequest("ChatId is required.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var messages = await _chatService.GetChatHistoryAsync(chatId, userId);

            return Ok(messages);
        }

        [HttpGet("conversations")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> GetConversations()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            return Ok(await _chatService.GetConversationsAsync(userId));
        }

        [HttpPut("conversations/{chatId}/title")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> UpdateConversationTitle(string chatId, [FromBody] UpdateChatConversationRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            await _chatService.UpdateConversationTitleAsync(userId, chatId, request.Title);
            return Ok("Conversation title updated.");
        }

        [HttpDelete("conversations/{chatId}")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> DeleteConversation(string chatId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            await _chatService.DeleteConversationAsync(userId, chatId);
            return Ok("Conversation deleted.");
        }
    }
}
