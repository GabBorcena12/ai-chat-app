using AIChatApp.API.Model;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data;
using LLama;
using LLama.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AIChatApp.API.Service
{
    public class ApiChatService
    {
        private readonly AppDbContext _dbContext;
        private readonly InteractiveExecutor _executor;
        private readonly ChatPaths _paths;
        private readonly string _assistantName;
        private readonly string _apiSystemContext;
        private readonly string _messageUnableToGenerateResponse = "Sorry unable to generate response. Please try again.";
        
        public ApiChatService(InteractiveExecutor executor, AppDbContext dbContext)
        {
            _dbContext = dbContext;
            _executor = executor;
            _assistantName = "AI Assistant";

            // Initialize paths and load context/knowledge
            _paths = new ChatPaths();
            _apiSystemContext = _paths.LoadApiSystemContext();
        }

        #region API
        public async Task<string> GetAIResponseForAPI(CancellationToken cancellationToken, ChatRequest request)
        {
            var clean = string.Empty;
            if (string.IsNullOrWhiteSpace(request?.Prompt))
                return _messageUnableToGenerateResponse;

            // Save USER message
            await SaveMessage(request.ChatId, request.User ?? "User", request.Prompt);

            // Load history from DB
            var history = await BuildConversation(request.ChatId, maxMessages: 20);
            var finalPrompt = "System Context: " + _apiSystemContext + "\n Chat History: " + history + "\n Prompt: " + request.Prompt;

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 150,
                AntiPrompts = new List<string> { $"{request.User}:", $"{_assistantName}:", $"Anjey's Pet Supply:" }
            };

            var buffer = new StringBuilder();

            // Removed Task.Run + Wait
            await foreach (var token in _executor.InferAsync(finalPrompt, inferenceParams, cancellationToken))
            {
                buffer.Append(token);
            }

            Console.WriteLine($"Prompt Context : {finalPrompt}");

            var rawResponse = buffer.ToString();
            Console.WriteLine($"Raw Response: {rawResponse}");
            if (!string.IsNullOrWhiteSpace(rawResponse))
            {
                // clean raw response
                clean = CleanResponse(rawResponse, request.User, _assistantName);

                // Save AI response
                await SaveMessage(request.ChatId, _assistantName, clean);
            }
            else
            {
                // return generic message if response is empty
                clean = _messageUnableToGenerateResponse;
            }
            return clean;
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

        private async Task SaveMessage(string chatId, string role, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return; // skip empty messages

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

        private async Task<string> BuildConversation(string chatId, int maxMessages = 20)
        {
            var messages = await _dbContext.ChatMessages
                .Where(x => x.ChatId == chatId && x.Role != _assistantName)
                .OrderByDescending(x => x.CreatedAt)
                .Take(maxMessages)
                .OrderBy(x => x.CreatedAt) // chronological
                .ToListAsync();

            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                var cleanContent = CleanResponse(msg.Content);
                sb.AppendLine($"{msg.Role}: {cleanContent}");
            }
            return sb.ToString();
        }

        private string CleanResponse(string rawResponse, string user = "User", string aiAssistant = "AI Assistant")
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return string.Empty;

            string response = rawResponse;

            // Remove any leading non-alphanumeric characters (e.g., ", - etc.)
            response = Regex.Replace(response, @"^[^\w\d]+", "");

            // Normalize spaces before punctuation
            response = Regex.Replace(response, @"\s+([.,!?;:])", "$1");

            // Normalize multiple spaces
            response = Regex.Replace(response, @"\s+", " ").Trim();

            // Remove User, AI Assistant, and Response signatures from the text
            response = response.Replace($"{user}:", "")
                               .Replace($"{aiAssistant}:", "")
                               .Replace("Response:", "")
                               .Replace("Prompt:", "");

            // Final trim to clean up any leftover spaces
            response = response.Trim();

            return response;
        }
        #endregion
    }
}