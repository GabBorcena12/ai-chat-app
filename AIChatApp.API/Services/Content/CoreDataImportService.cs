using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIChatApp.API.Services.Content
{
    /// <summary>
    /// Imports JSON files from AIChatApp.Core/Data into SQL so the data is available in the database.
    /// The import is insert-only by default: existing rows are not overwritten, which protects Backoffice edits later.
    /// </summary>
    public class CoreDataImportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly AppDbContext _dbContext;
        private readonly ChatPaths _paths;
        private readonly ILogger<CoreDataImportService> _logger;

        public CoreDataImportService(
            AppDbContext dbContext,
            ChatPaths paths,
            ILogger<CoreDataImportService> logger)
        {
            _dbContext = dbContext;
            _paths = paths;
            _logger = logger;
        }

        public async Task<int> ImportCoreDataJsonAsync(CancellationToken cancellationToken = default)
        {
            var dataRoot = Path.Combine(_paths.ProjectRoot, "AIChatApp.Core", "Data");
            if (!Directory.Exists(dataRoot))
            {
                _logger.LogWarning("Core data folder was not found: {DataRoot}", dataRoot);
                return 0;
            }

            var jsonFiles = Directory
                .EnumerateFiles(dataRoot, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path)
                .ToList();

            var existingPathList = await _dbContext.CoreDataFiles
                .AsNoTracking()
                .Select(x => x.RelativePath)
                .ToListAsync(cancellationToken);
            var existingPaths = existingPathList.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var imported = 0;
            foreach (var filePath in jsonFiles)
            {
                var relativePath = Path.GetRelativePath(dataRoot, filePath).Replace('\\', '/');
                if (existingPaths.Contains(relativePath))
                {
                    continue;
                }

                var rawJson = await File.ReadAllTextAsync(filePath, cancellationToken);
                var metadata = BuildMetadata(relativePath, rawJson);
                _dbContext.CoreDataFiles.Add(new CoreDataFileEntity
                {
                    RelativePath = relativePath,
                    ContentKey = metadata.ContentKey,
                    Area = metadata.Area,
                    ProfileId = metadata.ProfileId,
                    ContentType = metadata.ContentType,
                    FileName = Path.GetFileName(filePath),
                    RawJson = rawJson,
                    Content = metadata.Content,
                    StructuredJson = metadata.StructuredJson,
                    IsPublished = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                imported++;
            }

            if (imported > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Imported {ImportedCount} Core data JSON file(s) into SQL.", imported);
            return imported;
        }

        private static CoreDataFileMetadata BuildMetadata(string relativePath, string rawJson)
        {
            var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var area = parts.FirstOrDefault() ?? "Data";
            string? profileId = null;
            var contentType = area;

            if (parts is ["Assistants", var assistantProfile, var assistantSection, ..])
            {
                area = "Assistant";
                profileId = assistantProfile;
                contentType = assistantSection;
            }

            var contentKey = Path.ChangeExtension(relativePath, null)?.Replace('\\', '/') ?? relativePath;
            var content = ExtractContent(rawJson);
            var structuredJson = ExtractStructuredJson(rawJson);

            return new CoreDataFileMetadata(
                contentKey,
                area,
                profileId,
                contentType,
                content,
                structuredJson);
        }

        private static string? ExtractContent(string rawJson)
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.TryGetProperty("content", out var contentProperty)
                && contentProperty.ValueKind == JsonValueKind.String)
            {
                return contentProperty.GetString();
            }

            return document.RootElement.GetRawText();
        }

        private static string? ExtractStructuredJson(string rawJson)
        {
            using var document = JsonDocument.Parse(rawJson);
            foreach (var propertyName in new[] { "items", "entries", "topics" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var property))
                {
                    return JsonSerializer.Serialize(property, JsonOptions);
                }
            }

            return null;
        }

        private sealed record CoreDataFileMetadata(
            string ContentKey,
            string Area,
            string? ProfileId,
            string ContentType,
            string? Content,
            string? StructuredJson);
    }
}
