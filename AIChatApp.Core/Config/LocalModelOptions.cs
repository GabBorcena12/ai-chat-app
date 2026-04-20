namespace AIChatApp.Core.Config
{
    public class LocalModelOptions
    {
        public const string SectionName = "LocalModel";

        public string FileName { get; set; } = "qwen2.5-3b-instruct-q4_k_m.gguf";

        public uint ContextSize { get; set; } = 5000;
    }
}
