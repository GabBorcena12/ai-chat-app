using AIChatApp.API.Model;
using AIChatApp.API.Services.Generic;
using AIChatApp.API.Services.LLM;
using AIChatApp.API.Services.Processing;
using AIChatApp.API.Services.Prompting;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Azure.Core;
using LLama;
using LLama.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System.Text;

namespace AIChatApp.API.Services.Orchestration
{
    public class ChatOrchestrator
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ChatOrchestrator> _logger;
        private readonly ILLMService _llm;
        private readonly IPromptBuilder _promptBuilder;
        private readonly ChatHistoryService _chatHistoryService;
        private readonly IResponseProcessor _processor;
        private readonly Func<InteractiveExecutor> _retryExecutorFactory;
        private string _messageUnableToGenerateResponse;
        private string _assistantName;
        private readonly IConfiguration _configuration;
        private List<string> _antiPrompts = new List<string>();

        public ChatOrchestrator(
            ILogger<ChatOrchestrator> logger,
            AppDbContext dbContext,
            ILLMService llm,
            IPromptBuilder promptBuilder,
            IResponseProcessor processor,
            ChatHistoryService chatHistoryService,
            Func<InteractiveExecutor> retryExecutorFactory, 
            IConfiguration configuration)
        {
            _retryExecutorFactory = retryExecutorFactory;
            _dbContext = dbContext;
            _logger = logger;
            _llm = llm;
            _chatHistoryService = chatHistoryService;
            _promptBuilder = promptBuilder;
            _processor = processor;
            _assistantName = "AI Assistant";
            _messageUnableToGenerateResponse = "Sorry unable to generate response. Please try again.";
            _configuration = configuration;
            _antiPrompts = _configuration.GetSection("ApiSettings:Services.AntiPrompts").Get<List<string>>() ?? _antiPrompts;
        }

        public async Task<string> AskAsync(ChatRequest request, CancellationToken token)
        {
            // 1️. Build system + history + user prompt
            var prompt = await _promptBuilder.BuildPromptAsync(request.ChatId, request.User, request.Prompt);

            // 2️. Get LLM stream (or single output)
            var buffer = new StringBuilder();
            await foreach (var tokenStr in _llm.GenerateAsync(request.User, prompt, 100, token))
            {
                buffer.Append(tokenStr);
            }
            var llmResponse = buffer.ToString();

            // 3️. Process / clean / apply Agent overrides
            var finalResponse = string.Empty;
            _logger.LogInformation("First Response : {1}", llmResponse);
            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                finalResponse = _processor.Clean(llmResponse, request.Prompt);

                // for incomplete response : retry once with a fix prompt to complete it
                if (_processor.IsIncomplete(finalResponse))
                {
                    finalResponse = await RetryAndFixResponse(finalResponse, request, token);
                }

                await _chatHistoryService.SaveMessage(request.ChatId, _assistantName, finalResponse);
            }
            else
            {
                // return generic message if response is empty
                finalResponse = _messageUnableToGenerateResponse;
            }
            return finalResponse;
        }

        private async Task<string> RetryAndFixResponse(string incompleteResponse, ChatRequest request, CancellationToken cancellationToken)
        {
            // Rebuild Prompt with original + incomplete response + instructions to fix/complete
            var retryPrompt = await _promptBuilder.RebuildPromptWithIncompleteResponseAsync(request.ChatId, request.User, request.Prompt, incompleteResponse);
            if (retryPrompt.Length > 5000)
            {
                _logger.LogWarning("Retry prompt too large, skipping retry.");
                return incompleteResponse;
            }

            // 2️. Get LLM stream (or single output)
            var retryBuffer = new StringBuilder();
            _antiPrompts.Add(request.User);
            var inferenceParams = new InferenceParams
            {
                MaxTokens = 150,
                AntiPrompts = _antiPrompts
            };

            // Fresh executor to avoid any state carryover from previous inference
            // Which could impact retries
            await foreach (var token in _retryExecutorFactory().InferAsync(
                retryPrompt.ToString(),
                inferenceParams,
                cancellationToken))
            {
                retryBuffer.Append(token);
            }

            // 3. Clean and return Response
            var llmResponse = retryBuffer.ToString();
            _logger.LogInformation("Second Response : {1}", llmResponse);
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                _logger.LogWarning("Retry returned empty. Using original response.");
                return incompleteResponse;
            }

            var cleaned = _processor.Clean(llmResponse, request.User);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                _logger.LogWarning("Cleaned retry is empty. Using original response.");
                return incompleteResponse;
            }
            return cleaned;
        }
    }
}
