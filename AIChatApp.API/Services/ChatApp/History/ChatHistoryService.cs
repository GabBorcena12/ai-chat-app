using AIChatApp.API.Models.ChatApp;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.API.Services.ChatApp.History
{
    /// <summary>
    /// Persists chat messages and user-owned conversation metadata, including continuation updates and soft deletion.
    /// Supply a user ID for every user-facing operation to preserve conversation isolation.
    /// </summary>
    public class ChatHistoryService
    {
        private readonly AppDbContext _dbContext;

        public ChatHistoryService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ChatMessage>> GetChatHistoryAsync(string chatId, string? userId = null, int maxMessages = 1000)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return [];

            var query = _dbContext.ChatMessagesTbl
                .Where(x => x.ChatId == chatId);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(x => x.UserId == userId);
            }

            var messages = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(maxMessages)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ChatMessage
                {
                    MessageId = x.MessageId ?? x.Id.ToString(),
                    User = x.Role,
                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return messages;
        }

        public async Task<List<ChatConversationSummary>> GetConversationsAsync(string userId, int maxConversations = 25, int maxMessagesPerConversation = 80)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var conversations = await _dbContext.ChatConversations
                .AsNoTracking()
                .Where(x => x.UserId == userId && !x.IsDeleted)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(maxConversations)
                .ToListAsync();

            if (conversations.Count == 0)
            {
                return [];
            }

            var chatIds = conversations.Select(x => x.ChatId).ToList();
            var messages = await _dbContext.ChatMessagesTbl
                .AsNoTracking()
                .Where(x => x.UserId == userId && chatIds.Contains(x.ChatId))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();
            var messagesByChatId = messages
                .GroupBy(x => x.ChatId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(message => message.CreatedAt)
                        .Take(maxMessagesPerConversation)
                        .OrderBy(message => message.CreatedAt)
                        .ToList());

            return conversations
                .Select(conversation => new ChatConversationSummary
                {
                    ChatId = conversation.ChatId,
                    Title = conversation.Title,
                    CreatedAt = conversation.CreatedAt,
                    UpdatedAt = conversation.UpdatedAt,
                    Messages = messagesByChatId.GetValueOrDefault(conversation.ChatId, [])
                        .Select(message => new ChatMessage
                        {
                            MessageId = message.MessageId ?? message.Id.ToString(),
                            User = message.Role,
                            Content = message.Content,
                            CreatedAt = message.CreatedAt
                        })
                        .ToList()
                })
                .ToList();
        }

        public async Task SaveMessage(
            string chatId,
            string role,
            string content,
            string? userId = null,
            string? username = null,
            string? messageId = null,
            string? conversationTitle = null)
        {
            // Saves one chat bubble for the current user. If the same message id is sent
            // again, update that saved bubble instead of creating a duplicate.
            if (string.IsNullOrWhiteSpace(content))
                return;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                await EnsureConversationAsync(chatId, userId, username, conversationTitle);
            }

            if (!string.IsNullOrWhiteSpace(messageId))
            {
                var existingMessage = await _dbContext.ChatMessagesTbl
                    .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ChatId == chatId && x.UserId == userId);

                if (existingMessage is not null)
                {
                    existingMessage.Role = role;
                    existingMessage.Content = content;
                    existingMessage.Username = username;
                    existingMessage.CreatedAt = DateTime.UtcNow;
                    await TouchConversationAsync(chatId, userId, conversationTitle);
                    await _dbContext.SaveChangesAsync();
                    return;
                }
            }

            var message = new ChatMessageEntity
            {
                ChatId = chatId,
                UserId = userId,
                Username = username,
                MessageId = string.IsNullOrWhiteSpace(messageId) ? null : messageId,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ChatMessagesTbl.Add(message);
            await TouchConversationAsync(chatId, userId, conversationTitle);
            await _dbContext.SaveChangesAsync();
        }

        public async Task AppendToLatestAssistantMessageAsync(string chatId, string appendedContent, string assistantRole, string? userId = null)
        {
            // Adds more text to the latest assistant bubble. This is useful when an answer
            // is continued or repaired after the first saved response.
            if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(appendedContent))
            {
                return;
            }

            var query = _dbContext.ChatMessagesTbl
                .Where(x => x.ChatId == chatId && x.Role == assistantRole);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(x => x.UserId == userId);
            }

            var lastAssistantMessage = await query
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
            await TouchConversationAsync(chatId, userId, null);
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateConversationTitleAsync(string userId, string chatId, string title)
        {
            // Renames the conversation shown in the sidebar for this user only.
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }

            var conversation = await _dbContext.ChatConversations
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ChatId == chatId);

            if (conversation is null)
            {
                return;
            }

            conversation.Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim();
            conversation.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteConversationAsync(string userId, string chatId)
        {
            // Hides the conversation from the sidebar without deleting the database row.
            // Keeping the row makes the action safer and preserves audit/history options.
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }

            var conversation = await _dbContext.ChatConversations
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ChatId == chatId);

            if (conversation is null)
            {
                return;
            }

            conversation.IsDeleted = true;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task EnsureConversationAsync(string chatId, string userId, string? username, string? title)
        {
            // Makes sure the sidebar has a parent conversation row before messages are saved.
            // Think of this as creating or refreshing the conversation header.
            if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var conversation = await _dbContext.ChatConversations
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ChatId == chatId);

            if (conversation is not null)
            {
                if (!string.IsNullOrWhiteSpace(username))
                {
                    conversation.Username = username;
                }

                if (!string.IsNullOrWhiteSpace(title) && conversation.Title == "New conversation")
                {
                    conversation.Title = title.Trim();
                }

                conversation.UpdatedAt = DateTime.UtcNow;
                return;
            }

            _dbContext.ChatConversations.Add(new ChatConversationEntity
            {
                ChatId = chatId,
                UserId = userId,
                Username = username ?? string.Empty,
                Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private async Task TouchConversationAsync(string chatId, string? userId, string? title)
        {
            // Marks the conversation as recently active so it can stay sorted correctly
            // in the sidebar. It can also set the first real title for a new conversation.
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var conversation = await _dbContext.ChatConversations
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ChatId == chatId);

            if (conversation is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(title) && conversation.Title == "New conversation")
            {
                conversation.Title = title.Trim();
            }

            conversation.UpdatedAt = DateTime.UtcNow;
        }
    }
}
