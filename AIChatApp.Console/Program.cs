using AIChatApp.Core.Config;
using AIChatApp.Console.Service;
using LLama;
using LLama.Common;

namespace AIChatApp.Console
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var config = new ChatConfig();
            var paths = new ChatPaths();

            // Load AI Model: Meta-llama
            var parameters = new ModelParams(paths.ModelFile) { ContextSize = 5000 };
            using var model = LLamaWeights.LoadFromFile(parameters);
            using var context = model.CreateContext(parameters);
            var executor = new InteractiveExecutor(context);

            // Initialize chat session and service
            var chat = new DataModels.ChatSession(config);
            var aiService = new ChatService(executor, config.ReplaceText, config.AssistantName);

            InitializeChat(config);
            while (true)
            {
                var input = GetUserInput(config.UserName);

                if (string.IsNullOrWhiteSpace(input)|| HandleCommand(input, chat))
                    continue;

                if (!chat.IsAllowedTopic(input))
                {
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"{config.AssistantName}: {config.SystemLimitError}");
                    System.Console.ResetColor();
                    continue;
                }

                // Add user input
                chat.AddUser(input, config.UserName);
                var prompt = aiService.GeneratePrompt(chat);
                var response = await aiService.GetAIResponseForConsole(prompt);
                // Add assistant response
                chat.AddAssistant(response, config.AssistantName);
            }
        }

        #region Utility Methods
        private static string GetUserInput(string username)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGreen;
            System.Console.Write($"{username}: ");
            System.Console.ResetColor();
            return System.Console.ReadLine()?.Trim() ?? "";
        }

        private static bool HandleCommand(string input, DataModels.ChatSession chat)
        {
            switch (input.ToLower())
            {
                case "/exit": Environment.Exit(0); return true;
                case "/clear": chat.Clear(); System.Console.WriteLine("Conversation cleared."); return true;
                case "/history": chat.PrintHistory(); return true;
                case "/save": chat.Save("chat_history.json"); System.Console.WriteLine("Chat saved."); return true;
                case "/load": chat.Load("chat_history.json"); System.Console.WriteLine("Chat loaded."); return true;
                default: return false;
            }
        }

        private static void InitializeChat(ChatConfig config)
        {
            // Console setup
            System.Console.Clear();
            System.Console.WriteLine($"{config.ChatBotName.ToUpper()}");
            System.Console.WriteLine("Commands: /exit /clear /history /save /load");
            System.Console.WriteLine("-------------------------------------------");

            // Assistant greeting
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine($"{config.AssistantName}: Hello! Welcome to GAJI Poultry Supply. How can I assist you today?");
            System.Console.ResetColor();
        }
        #endregion
    }
}