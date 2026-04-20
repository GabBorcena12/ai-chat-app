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
                return [];

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

        public async Task AppendToLatestAssistantMessageAsync(string chatId, string appendedContent, string assistantRole)
        {
            if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(appendedContent))
            {
                return;
            }

            var lastAssistantMessage = await _dbContext.ChatMessagesTbl
                .Where(x => x.ChatId == chatId && x.Role == assistantRole)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (lastAssistantMessage is null)
            {
                await SaveMessage(chatId, assistantRole, appendedContent);
                return;
            }

            var separator = string.IsNullOrWhiteSpace(lastAssistantMessage.Content) || char.IsWhiteSpace(lastAssistantMessage.Content[^1])
                ? string.Empty
                : " ";

            lastAssistantMessage.Content = $"{lastAssistantMessage.Content}{separator}{appendedContent}".Trim();
            lastAssistantMessage.CreatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }
}
