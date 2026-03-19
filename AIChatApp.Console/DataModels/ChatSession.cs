using AIChatApp.Core.Config;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIChatApp.Console.DataModels
{
    public class ChatSession
    {
        private readonly ChatConfig _config;
        public List<ChatMessage> Messages { get; private set; } = new();
        private const int MaxHistory = 1000;

        public ChatSession(ChatConfig config)
        {
            _config = config;
            ResetSystem();
        }
        private void ResetSystem()
        {
            Messages.Clear();
            Messages.Add(new ChatMessage("AI Assistant", "Hello! Welcome to GAJI Poultry Supply. How can I assist you today?"));
        }

        public void AddUser(string text, string user)
        {
            Messages.Add(new ChatMessage(user, text.Trim()));
        }

        public void AddAssistant(string text, string assistant)
        {
            text = CleanResponse(text); 
            Messages.Add(new ChatMessage(assistant, text));
            TrimHistory();
        }

        public string BuildPrompt(string assistant)
        {
            var sb = new StringBuilder();

            // Skip system message to avoid repeated greetings
            foreach (var m in Messages.Skip(1))
            {
                sb.Append(m.Role)
                  .Append(": ")
                  .AppendLine(m.Content);
            }

            sb.Append(assistant);
            return sb.ToString();
        }

        private void TrimHistory()
        {
            if (Messages.Count <= MaxHistory) return;
            var system = Messages.First();
            Messages = Messages.Skip(Messages.Count - MaxHistory).ToList();
            Messages.Insert(0, system);
        }

        private string CleanResponse(string text)
        {
            text = text.Replace("\r", "")
                       .Replace("\n", " ");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        public void Clear() { ResetSystem(); }

        public void Save(string path) { File.WriteAllText(path, JsonSerializer.Serialize(Messages, new JsonSerializerOptions { WriteIndented = true })); }

        public void Load(string path) { if (!File.Exists(path)) return; Messages = JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(path)) ?? new List<ChatMessage>(); }

        public void PrintHistory()
        {
            System.Console.WriteLine("\n---- HISTORY ----");
            foreach (var m in Messages) System.Console.WriteLine($"{m.Role}: {m.Content}");
            System.Console.WriteLine("-----------------\n");
        }

        public bool IsAllowedTopic(string input)
        {
            var paths = new ChatPaths();
            string filePath = paths.DisAllowedTopicsFile;

            if (!File.Exists(filePath))
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"ERROR: Disallowed topics file not found: {filePath}");
                System.Console.ResetColor();
                return true; // default to allowed if no disallowed list
            }

            var disallowedTopics = File.ReadAllLines(filePath)
                                       .Select(l => l.Trim().ToLower())
                                       .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                                       .ToArray();

            input = input.ToLower().Trim();

            // return true if input does NOT contain any disallowed topic
            return !disallowedTopics.Any(t => input.Contains(t));
        }

        //public bool IsAllowedTopic(string input)
        //{
        //    string projectRoot = Directory.GetParent(AppContext.BaseDirectory).Parent.Parent.Parent.FullName;
        //    string filePath = Path.Combine(projectRoot, "Data", "allowed_topics.txt");

        //    if (!File.Exists(filePath))
        //    {
        //        Console.ForegroundColor = ConsoleColor.Red;
        //        Console.WriteLine($"ERROR: Allowed topics file not found: {filePath}");
        //        Console.ResetColor();
        //        return false;
        //    }

        //    var allowedTopics = File.ReadAllLines(filePath)
        //                            .Select(l => l.Trim())
        //                            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
        //                            .ToArray();

        //    input = input.ToLower().Trim();
        //    return allowedTopics.Any(t => input.Contains(t));
        //}
    }
}