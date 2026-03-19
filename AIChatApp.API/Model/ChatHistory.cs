namespace AIChatApp.API.Model
{
    public class ChatHistory
    {
        public string ChatId { get; set; }
        public List<ChatMessage> Messages { get; set; }
    }

    public class ChatMessage
    {
        public string User { get; set; }
        public string Content { get; set; }
    }
}
