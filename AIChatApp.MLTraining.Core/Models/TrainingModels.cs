namespace AIChatApp.MLTraining.Models;

public sealed class TrainingExample
{
    public int Id { get; set; }
    public string SourceType { get; set; } = "Manual";
    public string SourceReference { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string BadResponse { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string Intent { get; set; } = "DocumentationQuestion";
    public string IssueType { get; set; } = "None";
    public string ReviewStatus { get; set; } = "Draft";
    public bool ApprovedForTraining { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string ReviewedBy { get; set; } = string.Empty;
}

public sealed class TrainingDataset
{
    public int Id { get; set; }
    public string Name { get; set; } = "DocumentationQuality";
    public int Version { get; set; }
    public int ExampleCount { get; set; }
    public List<int> ExampleIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TrainingJob
{
    public int Id { get; set; }
    public int DatasetId { get; set; }
    public string Status { get; set; } = "Queued";
    public string TriggeredBy { get; set; } = "admin";
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? Accuracy { get; set; }
    public double? F1Score { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class ModelVersion
{
    public int Id { get; set; }
    public int TrainingJobId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ResponseReviewResult
{
    public string IssueType { get; set; } = "Good";
    public string Intent { get; set; } = "DocumentationQuestion";
    public float Confidence { get; set; }
    public bool IsRisky => !string.Equals(IssueType, "Good", StringComparison.OrdinalIgnoreCase)
                           && Confidence >= 0.65f;
    public string Source { get; set; } = "Rules";
}

public sealed class ResponseReviewerOptions
{
    public const string SectionName = "ResponseReviewer";
    public bool Enabled { get; set; } = true;
    public string PublishedModelPath { get; set; } = "AIChatApp.MLTraining.Core/ReviewerModels/published-response-reviewer.zip";
    public string CandidateModelFolder { get; set; } = "AIChatApp.MLTraining.Core/ReviewerModels/Candidates";
}

public sealed class ReviewerWorkflowState
{
    public int ApprovedExamples { get; set; }
    public int DatasetCount { get; set; }
    public int JobCount { get; set; }
    public int ModelCount { get; set; }
    public string PublishedModelVersion { get; set; } = string.Empty;
    public string PublishedModelPath { get; set; } = string.Empty;
}
