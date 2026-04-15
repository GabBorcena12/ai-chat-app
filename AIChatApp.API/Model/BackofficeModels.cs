namespace AIChatApp.API.Model
{
    public class BackofficeUserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsConfirmed { get; set; }
        public bool IsDisabled { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public List<string> Roles { get; set; } = [];
    }

    public class ReviewReportedResponseRequest
    {
        public string ReviewStatus { get; set; } = "Reviewed";
        public string? ReviewCategory { get; set; }
        public string? ReviewNotes { get; set; }
        public string? ValidatedQuestion { get; set; }
        public string? ValidatedResponse { get; set; }
        public bool PromoteToKnowledge { get; set; }
        public string? KnowledgeEntryType { get; set; }
        public string? KnowledgeSourceName { get; set; }
        public string? KnowledgeTitle { get; set; }
        public string? KnowledgeSummary { get; set; }
        public List<string>? Aliases { get; set; }
        public List<string>? Keywords { get; set; }
        public List<string>? ContextLines { get; set; }
        public bool PublishKnowledge { get; set; } = true;
    }

    public class SavePromptTemplateRequest
    {
        public string ProfileId { get; set; } = "Documentation";
        public string TemplateName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsPublished { get; set; } = true;
    }

    public class SaveKnowledgeEntryRequest
    {
        public string ProfileId { get; set; } = "Documentation";
        public string EntryType { get; set; } = "Reference";
        public string SourceName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? Content { get; set; }
        public List<string>? Aliases { get; set; }
        public List<string>? Keywords { get; set; }
        public bool IsPublished { get; set; } = true;
        public int SortOrder { get; set; }
    }

    public class CreateBackofficeUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public List<string>? Roles { get; set; }
        public bool IsConfirmed { get; set; } = true;
        public bool IsDisabled { get; set; }
    }

    public class UpdateBackofficeUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public List<string>? Roles { get; set; }
        public bool IsConfirmed { get; set; }
        public bool IsDisabled { get; set; }
    }
}
