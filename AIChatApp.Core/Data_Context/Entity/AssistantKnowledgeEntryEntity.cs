namespace AIChatApp.Core.Data_Context.Entity
{
    public class AssistantKnowledgeEntryEntity
    {
        public int Id { get; set; }
        public required string ProfileId { get; set; }
        public required string EntryType { get; set; }
        public required string SourceName { get; set; }
        public required string Title { get; set; }
        public string? Summary { get; set; }
        public string? Content { get; set; }
        public string? AliasesJson { get; set; }
        public string? KeywordsJson { get; set; }
        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
