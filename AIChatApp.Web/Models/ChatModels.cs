using System.Text.Json.Serialization;

namespace AIChatApp.Web.Models;

public class ChatRequestPayload
{
    public string ChatId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ContextMode { get; set; } = "documentation";
}

public class ContinueChatRequestPayload
{
    public string ChatId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string OriginalPrompt { get; set; } = string.Empty;
    public string PartialResponse { get; set; } = string.Empty;
    public string ContextMode { get; set; } = "documentation";
}

public class ReportChatResponsePayload
{
    public string ChatId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public string AssistantResponse { get; set; } = string.Empty;
    public string? ContextMode { get; set; }
    public bool WasUpdated { get; set; }
}

public class ConversationViewModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New conversation";
    public bool HasCustomTitle { get; set; }
    public List<MessageViewModel> Messages { get; set; } = [];

    [JsonIgnore]
    public bool IsRenaming { get; set; }

    [JsonIgnore]
    public string DraftTitle { get; set; } = string.Empty;
}

public class MessageViewModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastUpdatedAt { get; set; }
}

public class StreamEvent
{
    public string EventName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ChatResponsePayload
{
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
}

public class BackofficeReportViewModel
{
    public int Id { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public string AssistantResponse { get; set; } = string.Empty;
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

public class BackofficeReviewPayload
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

public class PromptTemplateViewModel
{
    public int Id { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SavePromptTemplatePayload
{
    public string ProfileId { get; set; } = "Documentation";
    public string TemplateName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPublished { get; set; } = true;
}

public class KnowledgeEntryViewModel
{
    public int Id { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public string? AliasesJson { get; set; }
    public string? KeywordsJson { get; set; }
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveKnowledgeEntryPayload
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

public class SaveKnowledgeEntryResult
{
    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
}

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

public class BackofficeWorkflowSummaryViewModel
{
    public int PendingReports { get; set; }
    public int ReviewedReports { get; set; }
    public int ApprovedReports { get; set; }
    public int TrainingCandidates { get; set; }
    public int PublishedKnowledgeEntries { get; set; }
}

public class TrainingCandidateViewModel
{
    public int ReportId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string BadResponse { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Intent { get; set; } = "DocumentationQuestion";
    public string ReviewedBy { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }
    public bool IsPromotedToKnowledge { get; set; }
}

public class ReviewerWorkflowStateViewModel
{
    public int ApprovedExamples { get; set; }
    public int DatasetCount { get; set; }
    public int JobCount { get; set; }
    public int ModelCount { get; set; }
    public string PublishedModelVersion { get; set; } = string.Empty;
    public string PublishedModelPath { get; set; } = string.Empty;
}

public class SaveBackofficeUserPayload
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public bool IsConfirmed { get; set; } = true;
    public bool IsDisabled { get; set; }
}

public class ConversationWorkspaceState
{
    public List<ConversationViewModel> Conversations { get; set; } = [];
    public string? ActiveConversationId { get; set; }
}
