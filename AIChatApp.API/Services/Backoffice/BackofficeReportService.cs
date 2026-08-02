using AIChatApp.API.Models.Backoffice;
using AIChatApp.API.Services.ChatApp.Content;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIChatApp.API.Services.Backoffice;

/// <summary>
/// Queries and reviews reported chat responses and determines which approved reports are eligible for training.
/// Review transitions should retain reviewer identity, correction data, category, and promoted-knowledge links for auditability.
/// </summary>
public sealed class BackofficeReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _dbContext;
    private readonly IAssistantContentService _assistantContentService;

    public BackofficeReportService(AppDbContext dbContext, IAssistantContentService assistantContentService)
    {
        _dbContext = dbContext;
        _assistantContentService = assistantContentService;
    }

    public async Task<IReadOnlyList<ChatResponseReportEntity>> GetReportsAsync(string? status)
    {
        var query = _dbContext.ChatResponseReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.ReviewStatus == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync();
    }

    public async Task<BackofficeWorkflowSummaryViewModel> GetWorkflowSummaryAsync()
    {
        var reports = await _dbContext.ChatResponseReports.AsNoTracking().ToListAsync();
        var publishedKnowledgeCount = await _dbContext.AssistantKnowledgeEntries
            .AsNoTracking()
            .CountAsync(x => x.ProfileId == "Documentation" && x.IsPublished);

        return new BackofficeWorkflowSummaryViewModel
        {
            PendingReports = reports.Count(x => string.Equals(x.ReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase)),
            ReviewedReports = reports.Count(x => string.Equals(x.ReviewStatus, "Reviewed", StringComparison.OrdinalIgnoreCase)),
            ApprovedReports = reports.Count(x => string.Equals(x.ReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase)),
            TrainingCandidates = reports.Count(IsTrainingCandidate),
            PublishedKnowledgeEntries = publishedKnowledgeCount
        };
    }

    public async Task<IReadOnlyList<TrainingCandidateViewModel>> GetTrainingCandidatesAsync()
        => await _dbContext.ChatResponseReports
            .AsNoTracking()
            .Where(x => x.ReviewStatus == "Approved"
                && !string.IsNullOrEmpty(x.ValidatedResponse)
                && !string.IsNullOrEmpty(x.ReviewCategory))
            .OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt)
            .Take(200)
            .Select(x => new TrainingCandidateViewModel
            {
                ReportId = x.Id,
                Question = x.ValidatedQuestion ?? x.UserPrompt,
                BadResponse = x.AssistantResponse,
                CorrectAnswer = x.ValidatedResponse ?? string.Empty,
                IssueType = x.ReviewCategory ?? "Other",
                Intent = x.ContextMode ?? "DocumentationQuestion",
                ReviewedBy = x.ReviewedBy ?? string.Empty,
                ReviewedAt = x.ReviewedAt,
                IsPromotedToKnowledge = x.PromotedKnowledgeEntryId.HasValue
            })
            .ToListAsync();

    public async Task<BackofficeResult> ReviewReportAsync(int id, ReviewReportedResponseRequest request, string reviewer)
    {
        var report = await _dbContext.ChatResponseReports.FirstOrDefaultAsync(x => x.Id == id);
        if (report is null)
        {
            return BackofficeResult.NotFound("Report not found.");
        }

        report.ReviewStatus = string.IsNullOrWhiteSpace(request.ReviewStatus) ? "Reviewed" : request.ReviewStatus.Trim();
        report.ReviewCategory = request.ReviewCategory?.Trim();
        report.ReviewNotes = request.ReviewNotes?.Trim();
        report.ValidatedQuestion = request.ValidatedQuestion?.Trim();
        report.ValidatedResponse = request.ValidatedResponse?.Trim();
        report.ReviewedBy = reviewer;
        report.ReviewedAt = DateTime.UtcNow;

        if (request.PromoteToKnowledge)
        {
            var knowledgeEntry = BuildKnowledgeEntryFromReview(report, request);
            _dbContext.AssistantKnowledgeEntries.Add(knowledgeEntry);
            await _dbContext.SaveChangesAsync();
            report.PromotedKnowledgeEntryId = knowledgeEntry.Id;
            _assistantContentService.InvalidateProfileCache(knowledgeEntry.ProfileId);
        }

        await _dbContext.SaveChangesAsync();
        return BackofficeResult.Ok("Report review saved.");
    }

    public async Task<BackofficeResult> LinkPromotedKnowledgeAsync(int id, int knowledgeEntryId, string reviewer)
    {
        var report = await _dbContext.ChatResponseReports.FirstOrDefaultAsync(x => x.Id == id);
        if (report is null)
        {
            return BackofficeResult.NotFound("Report not found.");
        }

        if (!await _dbContext.AssistantKnowledgeEntries.AnyAsync(x => x.Id == knowledgeEntryId))
        {
            return BackofficeResult.NotFound("Knowledge entry not found.");
        }

        report.PromotedKnowledgeEntryId = knowledgeEntryId;
        report.ReviewedBy = reviewer;
        report.ReviewedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return BackofficeResult.Ok("Report linked to knowledge entry.");
    }

    private static bool IsTrainingCandidate(ChatResponseReportEntity report)
        => string.Equals(report.ReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(report.ReviewCategory);

    private static AssistantKnowledgeEntryEntity BuildKnowledgeEntryFromReview(ChatResponseReportEntity report, ReviewReportedResponseRequest request)
    {
        var entryType = string.IsNullOrWhiteSpace(request.KnowledgeEntryType) ? "QuickAnswer" : request.KnowledgeEntryType.Trim();
        var sourceName = string.IsNullOrWhiteSpace(request.KnowledgeSourceName)
            ? (string.Equals(entryType, "Topic", StringComparison.OrdinalIgnoreCase) ? "Faq" : "QuickAnswers")
            : request.KnowledgeSourceName.Trim();
        var validatedQuestion = request.ValidatedQuestion?.Trim();
        var validatedResponse = request.ValidatedResponse?.Trim();

        return new AssistantKnowledgeEntryEntity
        {
            ProfileId = "Documentation",
            EntryType = entryType,
            SourceName = sourceName,
            Title = string.IsNullOrWhiteSpace(request.KnowledgeTitle) ? validatedQuestion ?? report.UserPrompt : request.KnowledgeTitle.Trim(),
            Summary = request.KnowledgeSummary?.Trim(),
            Content = string.Equals(entryType, "Topic", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(request.ContextLines ?? BuildDefaultContextLines(validatedQuestion, validatedResponse), JsonOptions)
                : validatedResponse ?? report.AssistantResponse,
            AliasesJson = SerializeOptionalList(request.Aliases ?? BuildDefaultAliases(validatedQuestion, report.UserPrompt, entryType)),
            KeywordsJson = SerializeOptionalList(request.Keywords),
            IsPublished = request.PublishKnowledge,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = report.ReviewedBy ?? "admin",
            UpdatedBy = report.ReviewedBy ?? "admin"
        };
    }

    private static List<string> BuildDefaultAliases(string? validatedQuestion, string originalQuestion, string entryType)
        => string.Equals(entryType, "QuickAnswer", StringComparison.OrdinalIgnoreCase)
            ? [validatedQuestion ?? originalQuestion]
            : [];

    private static List<string> BuildDefaultContextLines(string? validatedQuestion, string? validatedResponse)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(validatedQuestion))
        {
            lines.Add(validatedQuestion.Trim());
        }

        if (!string.IsNullOrWhiteSpace(validatedResponse))
        {
            lines.Add(validatedResponse.Trim());
        }

        return lines;
    }

    private static string? SerializeOptionalList(List<string>? values)
    {
        var cleaned = values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return cleaned is { Count: > 0 } ? JsonSerializer.Serialize(cleaned, JsonOptions) : null;
    }
}
