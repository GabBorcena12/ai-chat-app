namespace AIChatApp.API.Services.ChatApp.Processing
{
    /// <summary>
    /// Defines deterministic cleanup and completeness checks for raw model output.
    /// </summary>
    public interface IResponseProcessor
    {
        string Clean(string rawResponse, string user = "User", string assistant = "AI Assistant");
        bool IsIncomplete(string response);
    }
}
