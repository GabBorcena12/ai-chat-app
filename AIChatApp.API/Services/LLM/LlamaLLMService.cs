using Azure.Core;
using LLama;
using LLama.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIChatApp.API.Services.LLM
{
    public class LlamaLLMService : ILLMService
    {
        private readonly InteractiveExecutor _executor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LlamaLLMService> _logger;
        private List<string> _antiPrompts = new List<string>();

        public LlamaLLMService(InteractiveExecutor executor, IConfiguration configuration, ILogger<LlamaLLMService> logger)
        {
            _executor = executor;
            _configuration = configuration;
            _logger = logger;
            _antiPrompts = _configuration.GetSection("ApiSettings:Services.AntiPrompts").Get<List<string>>() ?? _antiPrompts;
        }

        public async IAsyncEnumerable<string> GenerateAsync(string user, string prompt, int maxToken, CancellationToken token)
        {
            _antiPrompts.Add(user);
            var sw = Stopwatch.StartNew();

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxToken,
                AntiPrompts = _antiPrompts
            };

            await foreach (var tokenStr in _executor.InferAsync(prompt, inferenceParams, token))
            {
                yield return tokenStr;
            }

            sw.Stop();
            // Log model latency
            if (sw.ElapsedMilliseconds > 30000)
            {
                _logger.LogWarning($"⚠️ Model is slow (GenerateAsync): {sw.ElapsedMilliseconds} ms");
            }
            else
            {
                _logger.LogInformation($"LLM Success | Latency (GenerateAsync): {sw.ElapsedMilliseconds} ms");
            }            
        }
    }
}
