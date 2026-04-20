using System.Text.Json;

namespace AIChatApp.Core.Config
{
    public class ChatPaths
    {
        private static readonly Dictionary<string, CachedFileContent> FileCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new();

        public string ProjectRoot { get; }
        public string SystemContextFile { get; }
        public string ApiSystemContextFile { get; }
        public string ConsoleRoot { get; }
        public string SharedRoot { get; }
        public string ReadmeFile { get; }
        public string ProductKnowledgeFile { get; }
        public string DisAllowedTopicsFile { get; }
        public string ModelFile { get; }
        public string AssistantsRoot { get; }

        public ChatPaths(string modelFileName = "qwen2.5-3b-instruct-q4_k_m.gguf")
        {
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                ProjectRoot = "/app";
            }
            else
            {
                ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            }

            ConsoleRoot = ResolvePath("~/AIChatApp.Core/Data/Console");
            SharedRoot = ResolvePath("~/AIChatApp.Core/Data/Shared");
            SystemContextFile = ResolvePath("~/AIChatApp.Core/Data/Console/system_context.json");
            ApiSystemContextFile = ResolvePath("~/AIChatApp.Core/Data/Shared/system_api_context.json");
            ReadmeFile = ResolvePath("~/README.md");
            ProductKnowledgeFile = ResolvePath("~/AIChatApp.Core/Data/Console/product_knowledge.json");
            DisAllowedTopicsFile = ResolvePath("~/AIChatApp.Core/Data/Console/disallowed_topics.json");

            var normalizedModelFileName = modelFileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                ? modelFileName
                : $"{modelFileName}.gguf";

            ModelFile = ResolvePath($"~/AIChatApp.Core/Models/{normalizedModelFileName}");
            AssistantsRoot = ResolvePath("~/AIChatApp.Core/Data/Assistants");
        }

        private string ResolvePath(string path)
        {
            if (path.StartsWith("~", StringComparison.Ordinal))
            {
                path = path.Replace("~", ProjectRoot, StringComparison.Ordinal);
            }

            return Path.GetFullPath(path);
        }

        public string LoadSystemContext()
        {
            return LoadJsonContentFile(SystemContextFile, "System context");
        }

        public string LoadApiSystemContext()
        {
            return LoadJsonContentFile(ApiSystemContextFile, "API system context");
        }

        public string LoadReadme()
        {
            return LoadCachedFile(ReadmeFile, "README");
        }

        public string LoadProductKnowledge()
        {
            return LoadJsonContentFile(ProductKnowledgeFile, "Product knowledge");
        }

        public IReadOnlyList<string> LoadDisallowedTopics()
        {
            return LoadJsonFile<JsonStringListFile>(DisAllowedTopicsFile, "Disallowed topics").Items;
        }

        public string LoadAssistantPrompt(string profileId, string fileName)
        {
            var path = ResolvePath($"~/AIChatApp.Core/Data/Assistants/{profileId}/Prompts/{NormalizeJsonFileName(fileName)}");
            return LoadJsonContentFile(path, $"{profileId} prompt {fileName}");
        }

        public string LoadAssistantKnowledge(string profileId, string fileName)
        {
            var path = ResolvePath($"~/AIChatApp.Core/Data/Assistants/{profileId}/Knowledge/{NormalizeJsonFileName(fileName)}");
            return LoadJsonContentFile(path, $"{profileId} knowledge {fileName}");
        }

        public IReadOnlyList<JsonQuickAnswerEntry> LoadAssistantQuickAnswers(string profileId)
        {
            var path = ResolvePath($"~/AIChatApp.Core/Data/Assistants/{profileId}/Knowledge/QuickAnswers.json");
            return LoadJsonFile<JsonQuickAnswersFile>(path, $"{profileId} quick answers").Entries;
        }

        public IReadOnlyList<JsonTopicEntry> LoadAssistantTopics(string profileId)
        {
            var path = ResolvePath($"~/AIChatApp.Core/Data/Assistants/{profileId}/Knowledge/Faq.json");
            return LoadJsonFile<JsonTopicKnowledgeFile>(path, $"{profileId} topic knowledge").Topics;
        }

        private static string NormalizeJsonFileName(string fileName)
        {
            return Path.ChangeExtension(fileName, ".json");
        }

        private static string LoadJsonContentFile(string path, string label)
        {
            return LoadJsonFile<JsonContentFile>(path, label).Content ?? string.Empty;
        }

        private static T LoadJsonFile<T>(string path, string label) where T : class, new()
        {
            var raw = LoadCachedFile(path, label);
            try
            {
                return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{label} JSON is invalid: {path}", ex);
            }
        }

        private static string LoadCachedFile(string path, string label)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{label} file not found: {path}");
            }

            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);

            lock (CacheLock)
            {
                if (FileCache.TryGetValue(path, out var cached)
                    && cached.LastWriteTimeUtc == lastWriteTimeUtc)
                {
                    return cached.Content;
                }

                var content = File.ReadAllText(path);
                FileCache[path] = new CachedFileContent(content, lastWriteTimeUtc);
                return content;
            }
        }

        private sealed record CachedFileContent(string Content, DateTime LastWriteTimeUtc);
    }
}
