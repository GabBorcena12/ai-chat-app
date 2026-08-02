namespace AIChatApp.API.Models.ChatApp
{
    public class ReportChatResponseRequest
    {
        public string ChatId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string AssistantResponse { get; set; } = string.Empty;
        public string? ContextMode { get; set; }
        public bool WasUpdated { get; set; }
    }
}
