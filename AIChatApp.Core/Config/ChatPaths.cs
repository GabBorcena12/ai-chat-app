namespace AIChatApp.Core.Config
{
    public class ChatPaths
    {
        public string ProjectRoot { get; }
        public string SystemContextFile { get; }
        public string ApiSystemContextFile { get; }
        public string ProductKnowledgeFile { get; }
        public string DisAllowedTopicsFile { get; }
        public string ModelFile { get; }

        public ChatPaths(string modelFileName = "meta-llama-3.1-8b-instruct-q4_k_m.gguf")
        {
            // Resolve solution root (acts like "~")
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                // Running inside Docker
                ProjectRoot = "/app";
            }
            else
            {
                // Running on host machine
                ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            }

            // Use "~" style paths
            SystemContextFile = ResolvePath("~/AIChatApp.Core/Data/system_context.txt");
            ApiSystemContextFile = ResolvePath("~/AIChatApp.Core/Data/system_api_context.txt");
            ProductKnowledgeFile = ResolvePath("~/AIChatApp.Core/Data/product_knowledge.txt");
            DisAllowedTopicsFile = ResolvePath("~/AIChatApp.Core/Data/disallowed_topics.txt");
            ModelFile = ResolvePath($"~/AIChatApp.Core/Models/{modelFileName}");
        }

        private string ResolvePath(string path)
        {
            if (path.StartsWith("~"))
            {
                path = path.Replace("~", ProjectRoot);
            }

            return Path.GetFullPath(path);
        }

        public string LoadSystemContext()
        {
            if (!File.Exists(SystemContextFile))
                throw new FileNotFoundException($"System context file not found: {SystemContextFile}");
            return File.ReadAllText(SystemContextFile);
        }

        public string LoadApiSystemContext()
        {
            if (!File.Exists(ApiSystemContextFile))
                throw new FileNotFoundException($"System context file not found: {ApiSystemContextFile}");
            return File.ReadAllText(ApiSystemContextFile);
        }

        public string LoadProductKnowledge()
        {
            if (!File.Exists(ProductKnowledgeFile))
                throw new FileNotFoundException($"Product knowledge file not found: {ProductKnowledgeFile}");
            return File.ReadAllText(ProductKnowledgeFile);
        }
    }
}
