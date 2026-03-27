namespace AIChatApp.API.Services.LLM
{
    public interface ILLMService
    {
        IAsyncEnumerable<string> GenerateAsync(string user, string prompt, int maxToken, CancellationToken token);
    }
}
