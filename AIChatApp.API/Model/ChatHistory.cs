namespace AIChatApp.API.Model
{
    public class ChatHistory
    {
        public string ChatId { get; set; } = string.Empty;
        public List<ChatMessage> Messages { get; set; } = [];
    }

    public class ChatMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ChatConversationSummary
    {
        public string ChatId { get; set; } = string.Empty;
        public string Title { get; set; } = "New conversation";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ChatMessage> Messages { get; set; } = [];
    }

    public class UpdateChatConversationRequest
    {
        public string Title { get; set; } = string.Empty;
    }
}
