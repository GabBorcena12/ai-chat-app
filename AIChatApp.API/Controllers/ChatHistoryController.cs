using AIChatApp.API.Services.Generic;
using Microsoft.AspNetCore.Mvc;

namespace AIChatApp.API.Controllers
{
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
        public async Task<IActionResult> GetConversation(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return BadRequest("ChatId is required.");

            var messages = await _chatService.GetChatHistoryAsync(chatId);

            return Ok(messages);
        }
    }
}