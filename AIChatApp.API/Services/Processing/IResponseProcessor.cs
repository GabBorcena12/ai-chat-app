namespace AIChatApp.API.Services.Processing
{
    public interface IResponseProcessor
    {
        string Clean(string rawResponse, string user = "User", string assistant = "AI Assistant");
        bool IsIncomplete(string response);
    }
}
