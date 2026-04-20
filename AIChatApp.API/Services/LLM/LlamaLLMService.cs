using AIChatApp.Core.Config;
using Azure.Core;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIChatApp.API.Services.LLM
{
    public class LlamaLLMService : ILLMService
    {
        private readonly InteractiveExecutor _executor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LlamaLLMService> _logger;
        private readonly AssistantProfileOptions _assistantProfile;
        private List<string> _antiPrompts = new();

        public LlamaLLMService(
            InteractiveExecutor executor,
            IConfiguration configuration,
            ILogger<LlamaLLMService> logger,
            IOptions<AssistantProfileOptions> assistantProfileOptions)
        {
            _executor = executor;
            _configuration = configuration;
            _logger = logger;
            _assistantProfile = assistantProfileOptions.Value;
            _antiPrompts = _configuration.GetSection("ApiSettings:Services.AntiPrompts").Get<List<string>>() ?? _antiPrompts;
        }

        public async IAsyncEnumerable<string> GenerateAsync(string user, string prompt, int maxToken, [EnumeratorCancellation] CancellationToken token)
        {
            var antiPrompts = new List<string>(_antiPrompts)
            {
                user,
                $"{user}:",
                "User:",
                $"{_assistantProfile.AssistantName}:",
                "Response:",
                "Prompt:",
                "Answer:"
            };
            var sw = Stopwatch.StartNew();

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxToken,
                AntiPrompts = antiPrompts.Distinct().ToList()
            };

            await foreach (var tokenStr in _executor.InferAsync(prompt, inferenceParams, token))
            {
                yield return tokenStr;
            }

            sw.Stop();
            if (sw.ElapsedMilliseconds > 30000)
            {
                _logger.LogWarning("Model is slow (GenerateAsync): {ElapsedMs} ms", sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation("LLM Success | Latency (GenerateAsync): {ElapsedMs} ms", sw.ElapsedMilliseconds);
            }
        }
    }
}
