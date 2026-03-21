using AIChatApp.API.Model;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data;
using LLama;
using LLama.Common;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
namespace AIChatApp.API.Service
{
    public class ApiChatService
    {
        private readonly AppDbContext _dbContext;
        private readonly InteractiveExecutor _executor;
        private readonly ChatPaths _paths;
        private readonly ILogger<ApiChatService> _logger;
        private readonly string _assistantName;
        private readonly string _apiSystemContext;
        private readonly string _messageUnableToGenerateResponse = "Sorry unable to generate response. Please try again.";
        
        public ApiChatService(InteractiveExecutor executor, AppDbContext dbContext, ILogger<ApiChatService>  logger)
        {
            _dbContext = dbContext;
            _executor = executor;
            _logger = logger;
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
            var finalPrompt = new StringBuilder();
            finalPrompt.AppendLine($"System: {_apiSystemContext}");
            finalPrompt.AppendLine();

            finalPrompt.AppendLine(history);

            finalPrompt.AppendLine($"User: {request.Prompt}");
            finalPrompt.AppendLine($"{_assistantName}:");

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 150,
                AntiPrompts = new List<string> { $"{request.User}:", $"{_assistantName}:", $"User:", $": " }
            };

            var buffer = new StringBuilder();

            await foreach (var token in _executor.InferAsync(finalPrompt.ToString(), inferenceParams, cancellationToken))
            {
                buffer.Append(token);
            }

            _logger.LogInformation($"Prompt Context : {finalPrompt}");

            var rawResponse = buffer.ToString();
            _logger.LogInformation($"Raw Response: {rawResponse}");

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