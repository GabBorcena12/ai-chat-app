namespace AIChatApp.API.Models.ChatApp
{
    public class ChatRequest
    {
        public string ChatId { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string? ContextMode { get; set; }
        public string? UserId { get; set; }
        public string? UserMessageId { get; set; }
        public string? AssistantMessageId { get; set; }
        public string? ConversationTitle { get; set; }
    }
}
