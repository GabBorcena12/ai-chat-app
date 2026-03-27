using AIChatApp.API.Services.Processing;
using System.Text.RegularExpressions;

namespace AIChatApp.Core.Agents
{
    public class AgentResponseProcessor : IResponseProcessor
    {
        private readonly AgentTools _tools;
        private readonly string _assistantName = "AI Assistant";

        public AgentResponseProcessor(AgentTools tools)
        {
            _tools = tools;
        }
        
        public string Process(string userMessage, string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return llmResponse;

            var msg = userMessage.ToLowerInvariant();

            switch (true)
            {
                // TODO : Add more keywords and tools as needed
                case bool _ when msg.Contains("Chicken"):
                case bool _ when msg.Contains("Bird"):
                case bool _ when msg.Contains("Pigeon"):
                case bool _ when msg.Contains("Rabbit"):
                    return _tools.SuggestProduct("Integra");
                default:
                    return llmResponse;
            }
        }

        public string Clean(string rawResponse, string user = "User", string assistant = "AI Assistant")
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(rawResponse))
                return string.Empty;

            string response = rawResponse
                .Replace("User:", "")
                .Replace($"{user}:", "")
                .Replace($"{_assistantName}:", "")
                .Replace("Note:", "")
                .Replace("Limit:", "")
                .Replace("Answer:", "");

            // Remove any leading non-alhanumeric characters (e.g., ", - etc.)
            response = Regex.Replace(response, @"^[^\w\d]+", "");

            // Normalize spaces before punctuation
            response = Regex.Replace(response, @"\s+([.,!?;:])", "$1");

            // Normalize multiple spaces
            response = Regex.Replace(response, @"\s+", " ").Trim();

            // Remove User, AI Assistant, and Response signatures from the text
            response = response.Replace($"{user}:", "")
                               .Replace($"{_assistantName}:", "")
                               .Replace("Response:", "")
                               .Replace("Prompt:", "");

            // Final trim to clean up any leftover spaces
            response = response.Trim();
            return Process(user, response);
        }

        public bool IsIncomplete(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return true;

            var trimmed = response.Trim();

            // Too short → likely bad
            if (trimmed.Length < 15)
                return true;

            // Must end with proper punctuation
            if (!Regex.IsMatch(trimmed, @"[.!?]$"))
                return true;

            // Ends with connector word → likely cut
            if (Regex.IsMatch(trimmed, @"\b(and|or|with|for|to|of|in)\s*$", RegexOptions.IgnoreCase))
                return true;

            // Ends with broken punctuation
            if (trimmed.EndsWith(",") || trimmed.EndsWith(":"))
                return true;

            // Contains "cut-off" signals
            if (trimmed.Contains("..."))
                return true;

            return false;
        }
    }
}