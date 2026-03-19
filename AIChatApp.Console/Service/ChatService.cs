using AIChatApp.Core.Config;
using LLama;
using LLama.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace AIChatApp.Console.Service
{
    public class ChatService
    {
        private readonly InteractiveExecutor _executor;
        private readonly List<string> _replaceText;
        private readonly string _assistantName;
        private readonly ChatPaths _paths;
        private readonly string _systemContext;
        private readonly string _apiSystemContext;
        private readonly string _productKnowledge;


        public ChatService(InteractiveExecutor executor, List<string> replaceText, string assistantName = "Assistant")
        {
            _executor = executor;
            _replaceText = replaceText;
            _assistantName = assistantName;

            // Initialize paths and load context/knowledge
            _paths = new ChatPaths();
            _systemContext = _paths.LoadSystemContext();
            _apiSystemContext = _paths.LoadApiSystemContext();
            _productKnowledge = _paths.LoadProductKnowledge();
        }

        #region Console
        public async Task<string> GetAIResponseForConsole(string prompt)
        {
            bool firstToken = true;
            var inferenceParams = new InferenceParams
            {
                //MaxTokens = 50,
                AntiPrompts = _replaceText
            };

            var buffer = new StringBuilder();
            using var cts = new CancellationTokenSource();

            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.ResetColor();

            var ellipsisTask = Task.Run(() => ShowEllipsisWithTimeout(cts.Token, 120000));

            int tokenCount = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var executorResult = _executor.InferAsync(prompt, inferenceParams);
                await foreach (var token in executorResult)
                {
                    tokenCount++;

                    if (firstToken)
                    {
                        firstToken = false;
                        cts.Cancel(); // stop the ellipsis

                        await ellipsisTask;

                        // Print assistant name
                        System.Console.ForegroundColor = ConsoleColor.DarkYellow;
                        System.Console.Write($"{_assistantName}: ");
                        System.Console.ResetColor();
                    }

                    // Clean token
                    string cleanToken = _replaceText
                        .Aggregate(token, (current, role) => current.Replace(role, ""));

                    if (!string.IsNullOrEmpty(cleanToken))
                    {
                        buffer.Append(cleanToken);
                        System.Console.Write(cleanToken);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.ResetColor();
            }

            stopwatch.Stop();

            // Final cleanup
            string response = buffer.ToString();

            // Fix broken words like "An j ey" → "Anjey"
            response = Regex.Replace(response, @"\b(\w)\s+(\w)\b", "$1$2");

            // Remove space before punctuation
            response = Regex.Replace(response, @"\s+([.,!?;:])", "$1");

            // Normalize spaces
            response = Regex.Replace(response, @"\s+", " ").Trim();
            return response;
        }

        public string GeneratePrompt(DataModels.ChatSession chat)
        {
            return _systemContext + "\n" + _productKnowledge + "\n" + chat.BuildPrompt(_assistantName);
        }

        private async Task ShowEllipsisWithTimeout(CancellationToken token, int timeoutMs = 60000)
        {
            int dotCount = 0;
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    string dots = new string('.', dotCount);
                    System.Console.Write($"\rThinking{dots}   ");
                    dotCount = (dotCount + 1) % 4;
                    await Task.Delay(500, linkedCts.Token).ContinueWith(_ => { });
                }
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"\n[DEBUG] Ellipsis exception: {ex.Message}");
                System.Console.ResetColor();
            }

            stopwatch.Stop();
            System.Console.Write("\r" + new string(' ', System.Console.WindowWidth) + "\r");

            if (timeoutCts.IsCancellationRequested)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("[DEBUG] Response timed out after 1 minute");
                System.Console.ResetColor();
            }
        }
        #endregion
    }
}