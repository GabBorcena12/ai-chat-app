namespace AIChatApp.Core.Data_Context.Entity
{
    public class CoreDataFileEntity
    {
        public int Id { get; set; }
        public required string RelativePath { get; set; }
        public required string ContentKey { get; set; }
        public required string Area { get; set; }
        public string? ProfileId { get; set; }
        public required string ContentType { get; set; }
        public required string FileName { get; set; }
        public required string RawJson { get; set; }
        public string? Content { get; set; }
        public string? StructuredJson { get; set; }
        public bool IsPublished { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
