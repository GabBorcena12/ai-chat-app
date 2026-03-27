using Azure.Core;
using LLama;
using LLama.Common;
using System.Runtime.CompilerServices;

namespace AIChatApp.API.Services.LLM
{
    public class LlamaLLMService : ILLMService
    {
        private readonly InteractiveExecutor _executor;

        public LlamaLLMService(InteractiveExecutor executor)
        {
            _executor = executor;
        }

        public async IAsyncEnumerable<string> GenerateAsync(string user, string prompt, int maxToken, CancellationToken token)
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxToken,
                AntiPrompts = new List<string> { $"{user}:", "AI Assistant:", $"User:", "Note:", "Limit:", "Answer:", "\n\n" }
            };

            await foreach (var tokenStr in _executor.InferAsync(prompt, inferenceParams, token))
            {
                yield return tokenStr;
            }
        }
    }
}
