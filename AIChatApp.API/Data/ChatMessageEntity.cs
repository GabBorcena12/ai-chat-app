namespace AIChatApp.Core.Data
{
    public class ChatMessageEntity
    {
        public int Id { get; set; }
        public required string ChatId { get; set; }
        public required string Role { get; set; }
        public required string Content { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
