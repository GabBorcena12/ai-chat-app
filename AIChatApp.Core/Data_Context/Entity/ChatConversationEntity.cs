namespace AIChatApp.Core.Data_Context.Entity
{
    public class ChatConversationEntity
    {
        public int Id { get; set; }
        public required string ChatId { get; set; }
        public required string UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Title { get; set; } = "New conversation";
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
