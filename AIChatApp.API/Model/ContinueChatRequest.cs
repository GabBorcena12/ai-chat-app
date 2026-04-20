namespace AIChatApp.API.Model
{
    public class ContinueChatRequest
    {
        public string ChatId { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string OriginalPrompt { get; set; } = string.Empty;
        public string PartialResponse { get; set; } = string.Empty;
        public string? ContextMode { get; set; }
    }
}
