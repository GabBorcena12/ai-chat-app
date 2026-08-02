using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIChatApp.API.Services.Backoffice;

/// <summary>
/// Coordinates reviewer dataset creation, ML.NET training, publication, and runtime model reloads.
/// Keep long-running training work out of controllers and publish a model only after a completed training job exists.
/// </summary>
public sealed class BackofficeReviewerService
{
    private const string AnsiReset = "\u001b[0m";
    private const string AnsiYellow = "\u001b[33m";
    private const string AnsiGreen = "\u001b[32m";
    private const string AnsiBlue = "\u001b[34m";
    private const string AnsiCyan = "\u001b[36m";
    private const string AnsiMagenta = "\u001b[35m";
    private readonly AppDbContext _dbContext;
    private readonly TrainingWorkspaceService _trainingWorkspace;
    private readonly ResponseReviewerService _responseReviewer;
    private readonly ILogger<BackofficeReviewerService> _logger;

    public BackofficeReviewerService(
        AppDbContext dbContext,
        TrainingWorkspaceService trainingWorkspace,
        ResponseReviewerService responseReviewer,
        ILogger<BackofficeReviewerService> logger)
    {
        _dbContext = dbContext;
        _trainingWorkspace = trainingWorkspace;
        _responseReviewer = responseReviewer;
        _logger = logger;
    }

    public ReviewerWorkflowState GetState() => _trainingWorkspace.GetState();

    public async Task<string> BuildDatasetAsync()
    {
        _logger.LogInformation("{LogLabel} Building reviewer dataset from approved reports and published knowledge entries.",
            LogLabel("[ML:DATASET:START]", AnsiBlue));

        var validatedReportedResponse = await LoadTrainingCandidateEntitiesAsync();
        var publishedKnowledgeEntries = await LoadPublishedKnowledgeTrainingEntitiesAsync();
        var importedReports = _trainingWorkspace.ImportApprovedExamples(validatedReportedResponse.Select(ToTrainingExample));
        var importedKnowledge = _trainingWorkspace.ImportPublishedKnowledgeEntries(
            publishedKnowledgeEntries.SelectMany(ToGoodTrainingExamples));
        var dataset = _trainingWorkspace.BuildDataset("DocumentationQualityReviewer");

        _logger.LogInformation(
            "{LogLabel} Dataset v{Version} built with {ExampleCount} approved example(s). Imported {ReportCount} report candidate(s) and {KnowledgeCount} published knowledge example(s).",
            LogLabel("[ML:DATASET:DONE]", AnsiGreen),
            dataset.Version,
            dataset.ExampleCount,
            importedReports,
            importedKnowledge);

        return $"Dataset v{dataset.Version} built with {dataset.ExampleCount} approved example(s). Imported {importedReports} report candidate(s) and {importedKnowledge} published knowledge example(s).";
    }

    public async Task<BackofficeResult> TrainAsync(string triggeredBy, CancellationToken cancellationToken)
    {
        var dataset = _trainingWorkspace.LatestDataset;
        if (dataset is null)
        {
            _logger.LogWarning("{LogLabel} Training requested before a reviewer dataset was built.",
                LogLabel("[ML:TRAIN:BLOCKED]", AnsiYellow));
            return BackofficeResult.BadRequest("Build a reviewer dataset before training.");
        }

        _logger.LogInformation(
            "{LogLabel} Training reviewer model from dataset v{Version} with {ExampleCount} approved example(s).",
            LogLabel("[ML:TRAIN:START]", AnsiCyan),
            dataset.Version,
            dataset.ExampleCount);

        var job = await _trainingWorkspace.QueueAndRunTrainingAsync(dataset.Id, triggeredBy, cancellationToken);
        if (job.Accuracy.HasValue && job.F1Score.HasValue)
        {
            _logger.LogInformation(
                "{LogLabel} Training job {JobId} completed. Accuracy: {Accuracy:P1}, F1: {F1:P1}.",
                LogLabel("[ML:TRAIN:DONE]", AnsiGreen),
                job.Id,
                job.Accuracy,
                job.F1Score);
            return BackofficeResult.Ok($"ML.NET reviewer trained. Accuracy: {job.Accuracy:P1}, F1: {job.F1Score:P1}.");
        }

        _logger.LogInformation("{LogLabel} Training job {JobId} completed. {Notes}",
            LogLabel("[ML:TRAIN:DONE]", AnsiGreen), job.Id, job.Notes);
        _logger.LogInformation(
            "{LogLabel} Validation metrics skipped for job {JobId}; add more balanced approved examples for reliable Accuracy/F1.",
            LogLabel("[ML:METRICS:SKIPPED]", AnsiYellow), job.Id);
        return BackofficeResult.Ok(job.Notes);
    }

    public BackofficeResult PublishLatest()
    {
        var model = _trainingWorkspace.LatestModel;
        if (model is null)
        {
            _logger.LogWarning("{LogLabel} Publish requested before a reviewer model was trained.",
                LogLabel("[ML:PUBLISH:BLOCKED]", AnsiYellow));
            return BackofficeResult.BadRequest("Train a reviewer model before publishing.");
        }

        _logger.LogInformation("{LogLabel} Publishing reviewer model {Version} from {ModelPath}.",
            LogLabel("[ML:PUBLISH:START]", AnsiMagenta), model.Version, model.ModelPath);
        _trainingWorkspace.PublishModel(model.Id);
        _responseReviewer.ReloadModel();
        _logger.LogInformation("{LogLabel} Published reviewer model {Version}; runtime reviewer cache reloaded.",
            LogLabel("[ML:PUBLISH:DONE]", AnsiGreen), model.Version);
        return BackofficeResult.Ok($"Published reviewer model {model.Version}. The API reviewer cache was refreshed and can now use it.");
    }

    private async Task<List<ChatResponseReportEntity>> LoadTrainingCandidateEntitiesAsync()
        => await _dbContext.ChatResponseReports
            .AsNoTracking()
            .Where(x => x.ReviewStatus == "Approved"
                && x.ReviewCategory != null
                && x.ReviewCategory != string.Empty)
            .OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt)
            .ToListAsync();

    private async Task<List<AssistantKnowledgeEntryEntity>> LoadPublishedKnowledgeTrainingEntitiesAsync()
        => await _dbContext.AssistantKnowledgeEntries
            .AsNoTracking()
            .Where(x => x.IsPublished && x.Content != null && x.Content != string.Empty)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync();

    private static TrainingExample ToTrainingExample(ChatResponseReportEntity report)
        => new()
        {
            SourceType = "ReviewedReport",
            SourceReference = $"Report-{report.Id}",
            Question = report.ValidatedQuestion ?? report.UserPrompt,
            BadResponse = report.AssistantResponse,
            ExpectedAnswer = string.Empty,
            IssueType = report.ReviewCategory ?? "Other",
            Intent = report.ContextMode ?? "DocumentationQuestion",
            ReviewStatus = "Approved",
            ApprovedForTraining = true,
            ReviewedBy = report.ReviewedBy ?? string.Empty,
            ReviewedAt = report.ReviewedAt
        };

    private static IEnumerable<TrainingExample> ToGoodTrainingExamples(AssistantKnowledgeEntryEntity entry)
    {
        var content = entry.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        var questions = new List<string> { entry.Title };
        questions.AddRange(DeserializeOptionalList(entry.AliasesJson));
        foreach (var question in questions.Where(question => !string.IsNullOrWhiteSpace(question)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new TrainingExample
            {
                SourceType = "PublishedKnowledgeEntry",
                SourceReference = $"Knowledge-{entry.Id}-{NormalizeTrainingSourceKey(question)}",
                Question = question.Trim(),
                BadResponse = content,
                ExpectedAnswer = content,
                IssueType = "Good",
                Intent = string.IsNullOrWhiteSpace(entry.SourceName) ? entry.EntryType : entry.SourceName,
                ReviewStatus = "Approved",
                ApprovedForTraining = true,
                ReviewedBy = entry.UpdatedBy ?? entry.CreatedBy ?? "knowledge",
                ReviewedAt = entry.UpdatedAt
            };
        }
    }

    private static List<string> DeserializeOptionalList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    private static string NormalizeTrainingSourceKey(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(32).ToArray();
        return chars.Length == 0 ? "entry" : new string(chars);
    }

    private static string LogLabel(string label, string color) => $"{color}{label}{AnsiReset}";
}
