namespace AIChatApp.API.Services.Prompting
{
    public interface IPromptBuilder
    {
        Task<string> BuildPromptAsync(string chatId, string user, string message, string? contextMode = null);
        Task<string> BuildContinuationPromptAsync(
            string chatId,
            string user,
            string originalPrompt,
            string partialResponse,
            string? contextMode = null);
        Task<string> RebuildPromptWithIncompleteResponseAsync(
            string chatId,
            string user,
            string message,
            string incompleteResponse,
            string? contextMode = null);
    }
}
