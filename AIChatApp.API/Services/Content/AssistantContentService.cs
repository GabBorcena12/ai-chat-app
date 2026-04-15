using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIChatApp.API.Services.Content
{
    public class AssistantContentService : IAssistantContentService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly AppDbContext _dbContext;
        private readonly ChatPaths _paths;

        public AssistantContentService(AppDbContext dbContext, ChatPaths paths)
        {
            _dbContext = dbContext;
            _paths = paths;
        }

        public async Task<string> LoadPromptAsync(string profileId, string templateName, CancellationToken cancellationToken = default)
        {
            var normalizedTemplate = Path.GetFileNameWithoutExtension(templateName);
            var published = await _dbContext.AssistantPromptTemplates
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId && x.TemplateName == normalizedTemplate && x.IsPublished)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return published?.Content ?? _paths.LoadAssistantPrompt(profileId, templateName);
        }

        public async Task<string> LoadKnowledgeTextAsync(string profileId, string sourceName, CancellationToken cancellationToken = default)
        {
            var normalizedSource = Path.GetFileNameWithoutExtension(sourceName);

            if (string.Equals(normalizedSource, "QuickAnswers", StringComparison.OrdinalIgnoreCase))
            {
                var quickAnswers = await LoadQuickAnswersAsync(profileId, cancellationToken);
                if (quickAnswers.Count > 0)
                {
                    return string.Join(Environment.NewLine + Environment.NewLine, quickAnswers.Select(entry =>
                        $"Q: {string.Join(" | ", entry.Aliases)}{Environment.NewLine}A: {entry.Answer}"));
                }
            }

            if (string.Equals(normalizedSource, "Faq", StringComparison.OrdinalIgnoreCase))
            {
                var topics = await LoadTopicsAsync(profileId, cancellationToken);
                if (topics.Count > 0)
                {
                    return string.Join(Environment.NewLine + Environment.NewLine, topics.Select(entry =>
                        $"topic: {entry.Topic}{Environment.NewLine}keywords: {string.Join(", ", entry.Keywords)}{Environment.NewLine}summary: {entry.Summary}{Environment.NewLine}context:{Environment.NewLine}{string.Join(Environment.NewLine, entry.Context)}"));
                }
            }

            var published = await _dbContext.AssistantKnowledgeEntries
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId
                    && x.SourceName == normalizedSource
                    && x.IsPublished
                    && x.EntryType == "Reference")
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            if (published.Count == 0)
            {
                return _paths.LoadAssistantKnowledge(profileId, sourceName);
            }

            return string.Join(Environment.NewLine + Environment.NewLine, published.Select(x => x.Content).Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public async Task<IReadOnlyList<JsonQuickAnswerEntry>> LoadQuickAnswersAsync(string profileId, CancellationToken cancellationToken = default)
        {
            var published = await _dbContext.AssistantKnowledgeEntries
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId
                    && x.EntryType == "QuickAnswer"
                    && x.IsPublished)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            if (published.Count == 0)
            {
                return _paths.LoadAssistantQuickAnswers(profileId);
            }

            return published.Select(x => new JsonQuickAnswerEntry
            {
                Aliases = DeserializeList(x.AliasesJson),
                Answer = x.Content ?? string.Empty
            }).ToList();
        }

        public async Task<IReadOnlyList<JsonTopicEntry>> LoadTopicsAsync(string profileId, CancellationToken cancellationToken = default)
        {
            var published = await _dbContext.AssistantKnowledgeEntries
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId
                    && x.EntryType == "Topic"
                    && x.IsPublished)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            if (published.Count == 0)
            {
                return _paths.LoadAssistantTopics(profileId);
            }

            return published.Select(x => new JsonTopicEntry
            {
                Topic = x.Title,
                Summary = x.Summary ?? string.Empty,
                Keywords = DeserializeList(x.KeywordsJson),
                Context = DeserializeList(x.Content)
            }).ToList();
        }

        public async Task SeedProfileContentAsync(string profileId, CancellationToken cancellationToken = default)
        {
            await SeedPromptTemplatesAsync(profileId, cancellationToken);
            await SeedQuickAnswersAsync(profileId, cancellationToken);
            await SeedTopicsAsync(profileId, cancellationToken);
            await SeedReferenceFilesAsync(profileId, cancellationToken);
        }

        private async Task SeedPromptTemplatesAsync(string profileId, CancellationToken cancellationToken)
        {
            var templateNames = new[] { "SystemContext", "AnswerStyle", "RetryTemplate", "ContinuationTemplate" };
            foreach (var templateName in templateNames)
            {
                var exists = await _dbContext.AssistantPromptTemplates
                    .AnyAsync(x => x.ProfileId == profileId && x.TemplateName == templateName, cancellationToken);
                if (exists)
                {
                    continue;
                }

                _dbContext.AssistantPromptTemplates.Add(new AssistantPromptTemplateEntity
                {
                    ProfileId = profileId,
                    TemplateName = templateName,
                    Content = _paths.LoadAssistantPrompt(profileId, $"{templateName}.json"),
                    IsPublished = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "system",
                    UpdatedBy = "system"
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task SeedQuickAnswersAsync(string profileId, CancellationToken cancellationToken)
        {
            var exists = await _dbContext.AssistantKnowledgeEntries
                .AnyAsync(x => x.ProfileId == profileId && x.EntryType == "QuickAnswer", cancellationToken);
            if (exists)
            {
                return;
            }

            var entries = _paths.LoadAssistantQuickAnswers(profileId);
            var sortOrder = 0;
            foreach (var entry in entries)
            {
                _dbContext.AssistantKnowledgeEntries.Add(new AssistantKnowledgeEntryEntity
                {
                    ProfileId = profileId,
                    EntryType = "QuickAnswer",
                    SourceName = "QuickAnswers",
                    Title = entry.Aliases.FirstOrDefault() ?? "Quick answer",
                    Content = entry.Answer,
                    AliasesJson = JsonSerializer.Serialize(entry.Aliases, JsonOptions),
                    IsPublished = true,
                    SortOrder = sortOrder++,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "system",
                    UpdatedBy = "system"
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task SeedTopicsAsync(string profileId, CancellationToken cancellationToken)
        {
            var exists = await _dbContext.AssistantKnowledgeEntries
                .AnyAsync(x => x.ProfileId == profileId && x.EntryType == "Topic", cancellationToken);
            if (exists)
            {
                return;
            }

            var entries = _paths.LoadAssistantTopics(profileId);
            var sortOrder = 0;
            foreach (var entry in entries)
            {
                _dbContext.AssistantKnowledgeEntries.Add(new AssistantKnowledgeEntryEntity
                {
                    ProfileId = profileId,
                    EntryType = "Topic",
                    SourceName = "Faq",
                    Title = entry.Topic,
                    Summary = entry.Summary,
                    Content = JsonSerializer.Serialize(entry.Context, JsonOptions),
                    KeywordsJson = JsonSerializer.Serialize(entry.Keywords, JsonOptions),
                    IsPublished = true,
                    SortOrder = sortOrder++,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "system",
                    UpdatedBy = "system"
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task SeedReferenceFilesAsync(string profileId, CancellationToken cancellationToken)
        {
            var sourceFiles = new[] { "Architecture", "Auth", "ChatEndpoints", "ConfigReference", "Docker", "ModelReference", "Troubleshooting" };
            foreach (var sourceFile in sourceFiles)
            {
                var exists = await _dbContext.AssistantKnowledgeEntries
                    .AnyAsync(x => x.ProfileId == profileId && x.EntryType == "Reference" && x.SourceName == sourceFile, cancellationToken);
                if (exists)
                {
                    continue;
                }

                try
                {
                    _dbContext.AssistantKnowledgeEntries.Add(new AssistantKnowledgeEntryEntity
                    {
                        ProfileId = profileId,
                        EntryType = "Reference",
                        SourceName = sourceFile,
                        Title = sourceFile,
                        Content = _paths.LoadAssistantKnowledge(profileId, $"{sourceFile}.json"),
                        IsPublished = true,
                        SortOrder = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        CreatedBy = "system",
                        UpdatedBy = "system"
                    });
                }
                catch (FileNotFoundException)
                {
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static List<string> DeserializeList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return json.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }
    }
}
