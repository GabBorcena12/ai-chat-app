using AIChatApp.API.Models.Backoffice;
using AIChatApp.API.Services.ChatApp.Content;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace AIChatApp.API.Services.Backoffice;

/// <summary>
/// Manages editable prompt templates and knowledge entries for Backoffice workflows.
/// Preserve duplicate checks, publication rules, and assistant-content cache invalidation whenever write behavior changes.
/// </summary>
public sealed class BackofficeContentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _dbContext;
    private readonly ChatPaths _chatPaths;
    private readonly IAssistantContentService _assistantContentService;
    private readonly ILogger<BackofficeContentService> _logger;

    public BackofficeContentService(
        AppDbContext dbContext,
        ChatPaths chatPaths,
        IAssistantContentService assistantContentService,
        ILogger<BackofficeContentService> logger)
    {
        _dbContext = dbContext;
        _chatPaths = chatPaths;
        _assistantContentService = assistantContentService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AssistantPromptTemplateEntity>> GetPromptTemplatesAsync(string profileId)
    {
        try
        {
            var templates = await _dbContext.AssistantPromptTemplates
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId)
                .OrderBy(x => x.TemplateName)
                .ToListAsync();

            if (templates.Count > 0)
            {
                return templates;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load prompt templates from the database for profile {ProfileId}. Falling back to file-backed defaults.", profileId);
        }

        return BuildFallbackPromptTemplates(profileId);
    }

    public async Task<BackofficeResult> UpdatePromptTemplateAsync(int id, SavePromptTemplateRequest request, string updatedBy)
    {
        var template = await _dbContext.AssistantPromptTemplates.FirstOrDefaultAsync(x => x.Id == id);
        if (template is null)
        {
            return BackofficeResult.NotFound("Prompt template not found.");
        }

        var originalProfileId = template.ProfileId;
        template.ProfileId = request.ProfileId.Trim();
        template.TemplateName = Path.GetFileNameWithoutExtension(request.TemplateName.Trim());
        template.Content = request.Content ?? string.Empty;
        template.IsPublished = request.IsPublished;
        template.UpdatedAt = DateTime.UtcNow;
        template.UpdatedBy = updatedBy;

        await _dbContext.SaveChangesAsync();
        _assistantContentService.InvalidateProfileCache(originalProfileId);
        _assistantContentService.InvalidateProfileCache(template.ProfileId);
        return BackofficeResult.Ok("Prompt template updated.");
    }

    public async Task<IReadOnlyList<AssistantKnowledgeEntryEntity>> GetKnowledgeAsync(string profileId, string? entryType)
    {
        var query = _dbContext.AssistantKnowledgeEntries
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId);

        if (!string.IsNullOrWhiteSpace(entryType))
        {
            query = query.Where(x => x.EntryType == entryType);
        }

        return await query
            .OrderBy(x => x.EntryType)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync();
    }

    public async Task<BackofficeResult> CreateKnowledgeAsync(SaveKnowledgeEntryRequest request, string createdBy)
    {
        var duplicate = await FindDuplicateKnowledgeEntryAsync(request);
        if (duplicate is not null)
        {
            return BackofficeResult.Conflict($"Knowledge entry already exists: {duplicate.Title}");
        }

        var entry = BuildKnowledgeEntry(request);
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = entry.CreatedAt;
        entry.CreatedBy = createdBy;
        entry.UpdatedBy = createdBy;

        _dbContext.AssistantKnowledgeEntries.Add(entry);
        await _dbContext.SaveChangesAsync();
        _assistantContentService.InvalidateProfileCache(entry.ProfileId);
        return BackofficeResult.Ok(new SaveKnowledgeEntryResponse
        {
            Id = entry.Id,
            Message = "Knowledge entry created."
        });
    }

    public async Task<BackofficeResult> UpdateKnowledgeAsync(int id, SaveKnowledgeEntryRequest request, string updatedBy)
    {
        var entry = await _dbContext.AssistantKnowledgeEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null)
        {
            return BackofficeResult.NotFound("Knowledge entry not found.");
        }

        var originalProfileId = entry.ProfileId;
        entry.ProfileId = request.ProfileId.Trim();
        entry.EntryType = request.EntryType.Trim();
        entry.SourceName = request.SourceName.Trim();
        entry.Title = request.Title.Trim();
        entry.Summary = request.Summary?.Trim();
        entry.Content = NormalizeKnowledgeContent(request);
        entry.AliasesJson = SerializeOptionalList(request.Aliases);
        entry.KeywordsJson = SerializeOptionalList(request.Keywords);
        entry.IsPublished = request.IsPublished;
        entry.SortOrder = request.SortOrder;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedBy = updatedBy;

        await _dbContext.SaveChangesAsync();
        _assistantContentService.InvalidateProfileCache(originalProfileId);
        _assistantContentService.InvalidateProfileCache(entry.ProfileId);
        return BackofficeResult.Ok("Knowledge entry updated.");
    }

    private static AssistantKnowledgeEntryEntity BuildKnowledgeEntry(SaveKnowledgeEntryRequest request)
        => new()
        {
            ProfileId = request.ProfileId.Trim(),
            EntryType = request.EntryType.Trim(),
            SourceName = request.SourceName.Trim(),
            Title = request.Title.Trim(),
            Summary = request.Summary?.Trim(),
            Content = NormalizeKnowledgeContent(request),
            AliasesJson = SerializeOptionalList(request.Aliases),
            KeywordsJson = SerializeOptionalList(request.Keywords),
            IsPublished = request.IsPublished,
            SortOrder = request.SortOrder
        };

    private static string? NormalizeKnowledgeContent(SaveKnowledgeEntryRequest request)
    {
        if (string.Equals(request.EntryType, "Topic", StringComparison.OrdinalIgnoreCase))
        {
            var lines = request.Content?
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? [];
            return JsonSerializer.Serialize(lines, JsonOptions);
        }

        return request.Content?.Trim();
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

    private async Task<AssistantKnowledgeEntryEntity?> FindDuplicateKnowledgeEntryAsync(SaveKnowledgeEntryRequest request)
    {
        var profileId = string.IsNullOrWhiteSpace(request.ProfileId) ? "Documentation" : request.ProfileId.Trim();
        var entryType = string.IsNullOrWhiteSpace(request.EntryType) ? "Reference" : request.EntryType.Trim();
        var candidateKeys = BuildKnowledgeQuestionKeys(request.Title, request.Aliases);
        if (candidateKeys.Count == 0)
        {
            return null;
        }

        var entries = await _dbContext.AssistantKnowledgeEntries
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.EntryType == entryType)
            .ToListAsync();

        return entries.FirstOrDefault(entry =>
            BuildKnowledgeQuestionKeys(entry.Title, DeserializeOptionalList(entry.AliasesJson)).Overlaps(candidateKeys));
    }

    private static HashSet<string> BuildKnowledgeQuestionKeys(string? title, IEnumerable<string>? aliases)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddKnowledgeQuestionKey(keys, title);
        if (aliases is not null)
        {
            foreach (var alias in aliases)
            {
                AddKnowledgeQuestionKey(keys, alias);
            }
        }

        return keys;
    }

    private static void AddKnowledgeQuestionKey(HashSet<string> keys, string? value)
    {
        var normalized = NormalizeKnowledgeQuestionKey(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            keys.Add(normalized);
        }
    }

    private static string NormalizeKnowledgeQuestionKey(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in (value ?? string.Empty).ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

    private List<AssistantPromptTemplateEntity> BuildFallbackPromptTemplates(string profileId)
    {
        var templateNames = new[] { "AnswerStyle", "ContinuationTemplate", "RetryTemplate", "SystemContext" };
        var now = DateTime.UtcNow;
        return templateNames.Select((templateName, index) => new AssistantPromptTemplateEntity
        {
            Id = -(index + 1),
            ProfileId = profileId,
            TemplateName = templateName,
            Content = _chatPaths.LoadAssistantPrompt(profileId, $"{templateName}.json"),
            IsPublished = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "fallback",
            UpdatedBy = "fallback"
        }).ToList();
    }
}
