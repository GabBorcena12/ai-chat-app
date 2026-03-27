using AIChatApp.API.Model;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.API.Services.Generic
{
    public class ChatHistoryService
    {
        private readonly AppDbContext _dbContext;

        public ChatHistoryService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ChatMessage>> GetChatHistoryAsync(string chatId, int maxMessages = 1000)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return new List<ChatMessage>();

            var messages = await _dbContext.ChatMessagesTbl
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(maxMessages)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ChatMessage
                {
                    User = x.Role,
                    Content = x.Content
                })
                .ToListAsync();

            return messages;
        }

        public async Task SaveMessage(string chatId, string role, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            var message = new ChatMessageEntity
            {
                ChatId = chatId,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ChatMessagesTbl.Add(message);
            await _dbContext.SaveChangesAsync();
        }
    }
}
