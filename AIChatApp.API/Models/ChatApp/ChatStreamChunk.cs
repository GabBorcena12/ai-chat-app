namespace AIChatApp.API.Models.ChatApp
{
    public class ChatStreamChunk
    {
        public required string Type { get; set; }
        public required string Content { get; set; }
    }
}
