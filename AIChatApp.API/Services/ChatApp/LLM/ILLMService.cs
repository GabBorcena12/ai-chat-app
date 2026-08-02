namespace AIChatApp.API.Services.ChatApp.LLM
{
    /// <summary>
    /// Abstracts token-by-token text generation so orchestration does not depend on a specific local model runtime.
    /// </summary>
    public interface ILLMService
    {
        IAsyncEnumerable<string> GenerateAsync(string user, string prompt, int maxToken, CancellationToken token);
    }
}
