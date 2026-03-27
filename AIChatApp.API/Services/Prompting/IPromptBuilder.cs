namespace AIChatApp.API.Services.Prompting
{
    public interface IPromptBuilder
    {
        Task<string> BuildPromptAsync(string chatId, string user, string message);
        Task<string> RebuildPromptWithIncompleteResponseAsync(
            string chatId,
            string user,
            string message,
            string incompleteResponse);
    }
}
