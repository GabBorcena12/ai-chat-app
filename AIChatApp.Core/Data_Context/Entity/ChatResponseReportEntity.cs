namespace AIChatApp.Core.Data_Context.Entity
{
    public class ChatResponseReportEntity
    {
        public int Id { get; set; }
        public required string ChatId { get; set; }
        public required string MessageId { get; set; }
        public required string Username { get; set; }
        public required string UserPrompt { get; set; }
        public required string AssistantResponse { get; set; }
        public string? ContextMode { get; set; }
        public bool WasUpdated { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ReviewStatus { get; set; } = "Pending";
        public string? ReviewCategory { get; set; }
        public string? ReviewNotes { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ValidatedQuestion { get; set; }
        public string? ValidatedResponse { get; set; }
        public int? PromotedKnowledgeEntryId { get; set; }
    }
}
