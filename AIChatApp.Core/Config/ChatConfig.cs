namespace AIChatApp.Core.Config
{
    public class ChatConfig
    {
        // Names
        public string AssistantName { get; set; } = "AI Assistant";
        public string UserName { get; set; } = "User";
        public string ChatBotName { get; set; } = "Gaji Chatbot";

        // System messages
        public string SystemLimitError { get; set; } = "Sorry, I can only discuss business related topics.";

        public List<string> ReplaceText { get; set; }

        // Constructor
        public ChatConfig()
        {
            ReplaceText = new List<string> { UserName, "( An j ey 's Pet Supply ):" };
        }
    }
}
