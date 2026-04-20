namespace AIChatApp.Core.Data_Context.Entity
{
    public class AssistantPromptTemplateEntity
    {
        public int Id { get; set; }
        public required string ProfileId { get; set; }
        public required string TemplateName { get; set; }
        public required string Content { get; set; }
        public bool IsPublished { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
