using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using AIChatApp.API.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace AIChatApp.API.Services.Prompting
{
    public class PromptBuilder : IPromptBuilder
    {
        private const int DefaultHistoryCount = 10;
        private const int DocumentationHistoryCount = 2;
        private const int DocumentationSectionCharLimit = 1200;
        private const int DocumentationSnippetCharLimit = 320;
        private const int DocumentationMaxSnippets = 3;
        private readonly AppDbContext _db;
        private readonly InventoryDbContext _inventorydb;
        private readonly IConfiguration _config;
        private readonly string _keyword;
        private readonly AssistantProfileOptions _assistantProfile;
        private readonly IAssistantContentService _assistantContentService;

        public PromptBuilder(
            AppDbContext db,
            InventoryDbContext inventorydb,
            IConfiguration configuration,
            IAssistantContentService assistantContentService,
            IOptions<AssistantProfileOptions> assistantProfileOptions)
        {
            _db = db;
            _inventorydb = inventorydb;
            _config = configuration;
            _keyword = _config.GetValue<string>("ApiSettings:Prompting.Keyword") ?? string.Empty;
            _assistantProfile = assistantProfileOptions.Value;
            _assistantContentService = assistantContentService;
        }

        public async Task<string> RebuildPromptWithIncompleteResponseAsync(
            string chatId,
            string user,
            string message,
            string incompleteResponse,
            string? contextMode = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"System: {await LoadSystemContextAsync(contextMode)}");
            sb.AppendLine();

            if (string.Equals(contextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                await AppendDocumentationKnowledgeAsync(sb, message);
            }

            var history = await _db.ChatMessagesTbl
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(GetHistoryCount(contextMode))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            foreach (var msg in history)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            sb.AppendLine(RenderPromptTemplate("RetryTemplate.json", new Dictionary<string, string>
            {
                ["INCOMPLETE_RESPONSE"] = incompleteResponse
            }));
            return sb.ToString();
        }

        public async Task<string> BuildContinuationPromptAsync(
            string chatId,
            string user,
            string originalPrompt,
            string partialResponse,
            string? contextMode = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"System: {await LoadSystemContextAsync(contextMode)}");
            sb.AppendLine();

            if (string.Equals(contextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                await AppendDocumentationKnowledgeAsync(sb, originalPrompt);
            }

            sb.AppendLine(RenderPromptTemplate("ContinuationTemplate.json", new Dictionary<string, string>
            {
                ["ORIGINAL_PROMPT"] = originalPrompt,
                ["PARTIAL_RESPONSE"] = partialResponse
            }));

            return sb.ToString();
        }

        public async Task<string> BuildPromptAsync(string chatId, string user, string message, string? contextMode = null)
        {
            var sb = new StringBuilder();
            var history = await _db.ChatMessagesTbl
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(GetHistoryCount(contextMode))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            sb.AppendLine($"System: {await LoadSystemContextAsync(contextMode)}");
            sb.AppendLine();

            if (string.Equals(contextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                await AppendDocumentationKnowledgeAsync(sb, message);
                sb.AppendLine(await _assistantContentService.LoadPromptAsync(_assistantProfile.ProfileId, "AnswerStyle.json"));
                sb.AppendLine();
            }

            var matches = await GetKeywordFromMessage(message);
            if (matches.Any())
            {
                sb.AppendLine("Relevant Knowledge:");
                foreach (var doc in matches)
                {
                    sb.AppendLine($"{doc.MasterSku} - {doc.ProductName} - {doc.ProductAlias}");
                }
                sb.AppendLine();
            }

            foreach (var msg in history)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            sb.AppendLine($"User: {message}");
            sb.AppendLine($"{_assistantProfile.AssistantName}:");

            return sb.ToString();
        }

        private async Task<string> LoadSystemContextAsync(string? contextMode)
        {
            if (string.Equals(contextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                return await _assistantContentService.LoadPromptAsync(_assistantProfile.ProfileId, "SystemContext.json");
            }

            return new ChatPaths().LoadApiSystemContext();
        }

        private async Task AppendDocumentationKnowledgeAsync(StringBuilder sb, string message)
        {
            var intent = ClassifyDocumentationIntent(message);
            var sections = (await GetDocumentationSectionsAsync(intent, message)).ToList();
            if (!sections.Any())
            {
                return;
            }

            foreach (var section in sections)
            {
                sb.AppendLine(section);
                sb.AppendLine();
            }
        }

        private async Task<IEnumerable<string>> GetDocumentationSectionsAsync(DocumentationIntent intent, string message)
        {
            var quickAnswers = await GetRelevantQuickAnswersAsync(intent, message);
            var sections = new List<string>();
            if (!string.IsNullOrWhiteSpace(quickAnswers))
            {
                sections.Add($"Quick Answers:{Environment.NewLine}{quickAnswers}");
            }

            sections.AddRange(await GetRetrievedDocumentationSectionsAsync(intent, message));
            return sections;
        }

        private async Task<IEnumerable<string>> GetRetrievedDocumentationSectionsAsync(DocumentationIntent intent, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return [];
            }

            var candidates = new List<RetrievedSnippet>();
            foreach (var knowledgeFile in GetKnowledgeFilesForIntent(intent))
            {
                var content = await _assistantContentService.LoadKnowledgeTextAsync(_assistantProfile.ProfileId, knowledgeFile);
                candidates.AddRange(ExtractSnippets(knowledgeFile, content, message, intent));
            }

            return candidates
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Content.Length)
                .DistinctBy(x => $"{x.Source}|{x.Content}", StringComparer.OrdinalIgnoreCase)
                .Take(DocumentationMaxSnippets)
                .Select(snippet => $"{snippet.Source}:{Environment.NewLine}{LimitText(snippet.Content, DocumentationSnippetCharLimit)}")
                .ToList();
        }

        private IEnumerable<string> GetKnowledgeFilesForIntent(DocumentationIntent intent)
        {
            return intent switch
            {
                DocumentationIntent.Auth => ["Faq.json", "Auth.json", "ConfigReference.json"],
                DocumentationIntent.Chat => ["Faq.json", "ChatEndpoints.json", "Architecture.json", "ConfigReference.json"],
                DocumentationIntent.Model => ["Faq.json", "ModelReference.json", "ConfigReference.json"],
                DocumentationIntent.Config => ["Faq.json", "ConfigReference.json", "ModelReference.json", "Docker.json"],
                DocumentationIntent.Docker => ["Faq.json", "Docker.json", "ConfigReference.json"],
                DocumentationIntent.Troubleshooting => ["Faq.json", "Troubleshooting.json", "ConfigReference.json", "ModelReference.json"],
                DocumentationIntent.Architecture => ["Faq.json", "Architecture.json", "ChatEndpoints.json"],
                _ => ["Faq.json", "Architecture.json", "ConfigReference.json", "ModelReference.json", "ChatEndpoints.json"]
            };
        }

        private async Task<string> GetRelevantQuickAnswersAsync(DocumentationIntent intent, string message)
        {
            var entries = (await _assistantContentService.LoadQuickAnswersAsync(_assistantProfile.ProfileId))
                .Select(entry => new FaqEntry(string.Join(" | ", entry.Aliases), entry.Answer))
                .ToList();
            var filtered = entries
                .Select(entry => new
                {
                    Entry = entry,
                    Score = ScoreFaqEntry(entry, message, intent)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Question.Length)
                .Take(4)
                .Select(x => x.Entry)
                .ToList();

            if (filtered.Count == 0)
            {
                filtered = entries
                    .Where(entry => MatchesIntent(entry.Question, intent))
                    .Take(4)
                    .ToList();
            }

            if (filtered.Count == 0)
            {
                filtered = entries.Take(4).ToList();
            }

            var sb = new StringBuilder();
            foreach (var entry in filtered)
            {
                var primaryQuestion = entry.Question.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? entry.Question;
                sb.AppendLine($"Q: {primaryQuestion}");
                sb.AppendLine($"A: {LimitText(entry.Answer, 220)}");
            }

            return sb.ToString().TrimEnd();
        }

        private static DocumentationIntent ClassifyDocumentationIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return DocumentationIntent.General;
            }

            var normalized = Regex.Replace(message.ToLowerInvariant(), @"[^\w\s/\-]", " ");

            if (ContainsAny(normalized, "401", "invalid_token", "error", "issue", "problem", "debug", "debugging", "troubleshoot", "troubleshooting", "timeout", "slow", "cut off", "cutoff", "exception", "failed", "failure"))
            {
                return DocumentationIntent.Troubleshooting;
            }

            if (ContainsAny(normalized, "docker", "container", "compose", "mssqlserver", "host.docker.internal", "env", "environment", "connectionstring"))
            {
                return DocumentationIntent.Docker;
            }

            if (ContainsAny(normalized, "model", "llm", "gguf", "localmodel", "context size", "contextsize", "qwen", "llama"))
            {
                return DocumentationIntent.Model;
            }

            if (ContainsAny(normalized, "config", "appsettings", "setting", "settings", "gatewaybaseurl", "apikey", "api key", "localmodel filename", "assistantprofile", "connection string"))
            {
                return DocumentationIntent.Config;
            }

            if (ContainsAny(normalized, "jwt", "token", "login", "signin", "sign in", "register", "2fa", "otp", "auth", "authenticate", "authorization", "google authenticator"))
            {
                return DocumentationIntent.Auth;
            }

            if (ContainsAny(normalized, "ask-stream", "ask ai", "ask-ai", "ask continue", "ask-continue", "stream", "sse", "endpoint", "/api/chat", "/chat/", "chat history", "continue"))
            {
                return DocumentationIntent.Chat;
            }

            if (ContainsAny(normalized, "architecture", "project", "solution", "structure", "overview", "what is", "used for", "responsibility", "responsibilities", "which project"))
            {
                return DocumentationIntent.Architecture;
            }

            return DocumentationIntent.General;
        }

        private static bool ContainsAny(string value, params string[] keywords)
            => keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        private static bool MatchesIntent(string question, DocumentationIntent intent)
        {
            var normalized = Regex.Replace(question.ToLowerInvariant(), @"[^\w\s/\-]", " ");
            return intent switch
            {
                DocumentationIntent.Auth => ContainsAny(normalized, "auth", "login", "jwt", "2fa", "otp", "google authenticator"),
                DocumentationIntent.Chat => ContainsAny(normalized, "chat", "stream", "continue", "endpoint", "headers", "gateway"),
                DocumentationIntent.Model => ContainsAny(normalized, "model", "llm", "gguf", "qwen", "llama", "context size"),
                DocumentationIntent.Config => ContainsAny(normalized, "config", "appsettings", "setting", "gateway", "api key", "assistantprofile", "localmodel", "connection string"),
                DocumentationIntent.Docker => ContainsAny(normalized, "docker", "container", "sql server", "secrets", "environment"),
                DocumentationIntent.Troubleshooting => ContainsAny(normalized, "slow", "wrong", "cut off", "invalid", "error", "check first"),
                DocumentationIntent.Architecture => ContainsAny(normalized, "what is", "which project", "structured", "frontend", "backend", "routing", "shared infrastructure"),
                _ => true
            };
        }

        private static int ScoreFaqEntry(FaqEntry entry, string message, DocumentationIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return 0;
            }

            var normalizedMessage = NormalizeForMatch(message);
            var aliases = entry.Question
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var bestAliasScore = aliases
                .Select(alias => ScoreAlias(alias, normalizedMessage))
                .DefaultIfEmpty(0)
                .Max();

            if (bestAliasScore == 0 && !MatchesIntent(entry.Question, intent))
            {
                return 0;
            }

            if (MatchesIntent(entry.Question, intent))
            {
                bestAliasScore += 2;
            }

            return bestAliasScore;
        }

        private static int ScoreAlias(string alias, string normalizedMessage)
        {
            var normalizedAlias = NormalizeForMatch(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                return 0;
            }

            if (string.Equals(normalizedAlias, normalizedMessage, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (normalizedMessage.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase)
                || normalizedAlias.Contains(normalizedMessage, StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            var aliasTokens = normalizedAlias
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messageTokens = normalizedMessage
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var overlap = aliasTokens.Count(token => messageTokens.Contains(token));
            return overlap;
        }

        private IEnumerable<RetrievedSnippet> ExtractSnippets(string fileName, string content, string message, DocumentationIntent intent)
        {
            var normalizedMessage = NormalizeForMatch(message);
            if (string.Equals(fileName, "Faq.json", StringComparison.OrdinalIgnoreCase))
            {
                return _assistantContentService.LoadTopicsAsync(_assistantProfile.ProfileId).GetAwaiter().GetResult()
                    .Select(entry => new TopicEntry(entry.Topic, entry.Keywords, string.Join(Environment.NewLine, entry.Context)))
                    .Select(entry => new RetrievedSnippet(
                        $"Topic: {entry.Topic}",
                        entry.Context,
                        ScoreTopicEntry(entry, normalizedMessage, intent)))
                    .Where(x => x.Score > 0);
            }

            var chunks = SplitIntoChunks(content)
                .Select(chunk => new RetrievedSnippet(
                    GetKnowledgeLabel(fileName),
                    chunk,
                    ScoreTextChunk(chunk, normalizedMessage, intent)))
                .Where(x => x.Score > 0);

            return chunks;
        }

        private static IEnumerable<string> SplitIntoChunks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                yield break;
            }

            var sections = Regex.Split(content.Trim(), @"(?:\r?\n){2,}")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));

            foreach (var section in sections)
            {
                if (section.Length <= DocumentationSnippetCharLimit)
                {
                    yield return section;
                    continue;
                }

                var lines = section.Split(["\r\n", "\n"], StringSplitOptions.None)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                var builder = new StringBuilder();
                foreach (var line in lines)
                {
                    if (builder.Length > 0 && builder.Length + line.Length + 1 > DocumentationSnippetCharLimit)
                    {
                        yield return builder.ToString().Trim();
                        builder.Clear();
                    }

                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(line);
                }

                if (builder.Length > 0)
                {
                    yield return builder.ToString().Trim();
                }
            }
        }

        private static int ScoreTextChunk(string chunk, string normalizedMessage, DocumentationIntent intent)
        {
            var normalizedChunk = NormalizeForMatch(chunk);
            if (string.IsNullOrWhiteSpace(normalizedChunk))
            {
                return 0;
            }

            var score = 0;
            if (normalizedChunk.Contains(normalizedMessage, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            var messageTokens = normalizedMessage
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var chunkTokens = normalizedChunk
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            score += chunkTokens.Count(messageTokens.Contains) * 3;

            if (MatchesIntent(chunk, intent))
            {
                score += 5;
            }

            return score;
        }

        private static int ScoreTopicEntry(TopicEntry entry, string normalizedMessage, DocumentationIntent intent)
        {
            var score = ScoreTextChunk(entry.Context, normalizedMessage, intent);

            var normalizedTopic = NormalizeForMatch(entry.Topic);
            if (normalizedMessage.Contains(normalizedTopic, StringComparison.OrdinalIgnoreCase)
                || normalizedTopic.Contains(normalizedMessage, StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }

            foreach (var keyword in entry.Keywords)
            {
                var normalizedKeyword = NormalizeForMatch(keyword);
                if (string.IsNullOrWhiteSpace(normalizedKeyword))
                {
                    continue;
                }

                if (normalizedMessage.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }
            }

            return score;
        }

        private static string GetKnowledgeLabel(string fileName)
            => Path.GetFileNameWithoutExtension(fileName) switch
            {
                "Auth" => "Auth Reference",
                "ChatEndpoints" => "Chat Endpoint Reference",
                "ConfigReference" => "Config Reference",
                "ModelReference" => "Model Reference",
                "Docker" => "Docker Reference",
                "Troubleshooting" => "Troubleshooting Reference",
                "Architecture" => "Architecture Reference",
                _ => "Knowledge Reference"
            };

        private static string NormalizeForMatch(string value)
            => Regex.Replace(value.ToLowerInvariant(), @"[^\w\s/\-]", " ")
                .Replace("  ", " ")
                .Trim();

        private static int GetHistoryCount(string? contextMode)
            => string.Equals(contextMode, "documentation", StringComparison.OrdinalIgnoreCase)
                ? DocumentationHistoryCount
                : DefaultHistoryCount;

        private static string LimitText(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxChars)
            {
                return value;
            }

            return value[..maxChars];
        }

        private string RenderPromptTemplate(string fileName, IReadOnlyDictionary<string, string> replacements)
        {
            var template = _assistantContentService.LoadPromptAsync(_assistantProfile.ProfileId, fileName).GetAwaiter().GetResult();
            foreach (var replacement in replacements)
            {
                template = template.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty, StringComparison.Ordinal);
            }

            return template;
        }

        private async Task<List<ProductEntity>> GetKeywordFromMessage(string message)
        {
            if (string.IsNullOrEmpty(_keyword))
            {
                return [];
            }

            var matchedKeywords = _keyword
                .Where(k => message.Contains(k, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!matchedKeywords.Any())
            {
                return [];
            }

            var likeClauses = string.Join(" OR ",
                matchedKeywords.Select((k, i) => $"ProductName LIKE '%' + {{{i}}} + '%'"));

            var sql = $"SELECT TOP 5 * FROM Products WHERE {likeClauses}";

            return await _inventorydb.Products
                .FromSqlRaw(sql, matchedKeywords.Cast<object>().ToArray())
                .ToListAsync();
        }
    }

    internal enum DocumentationIntent
    {
        General,
        Architecture,
        Auth,
        Chat,
        Model,
        Config,
        Docker,
        Troubleshooting
    }

    internal sealed record FaqEntry(string Question, string Answer);
    internal sealed record RetrievedSnippet(string Source, string Content, int Score);
    internal sealed record TopicEntry(string Topic, IReadOnlyList<string> Keywords, string Context);
}
