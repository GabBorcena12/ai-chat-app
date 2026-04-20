using AIChatApp.API.Services.Processing;
using AIChatApp.Core.Config;
using AIChatApp.Core.Services;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AIChatApp.Core.Agents
{
    public class AgentResponseProcessor : IResponseProcessor
    {
        private readonly AgentTools _tools;
        private readonly string _assistantName;

        public AgentResponseProcessor(AgentTools tools, IOptions<AssistantProfileOptions> assistantProfileOptions)
        {
            _tools = tools;
            _assistantName = assistantProfileOptions.Value.AssistantName;
        }

        public string Process(string userMessage, string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return llmResponse;
            }

            var msg = userMessage.ToLowerInvariant();

            return msg switch
            {
                _ when msg.Contains("chicken") => _tools.SuggestProduct("Integra"),
                _ when msg.Contains("bird") => _tools.SuggestProduct("Integra"),
                _ when msg.Contains("pigeon") => _tools.SuggestProduct("Integra"),
                _ when msg.Contains("rabbit") => _tools.SuggestProduct("Integra"),
                _ => llmResponse
            };
        }

        public string Clean(string rawResponse, string user = "User", string assistant = "AI Assistant")
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return string.Empty;
            }

            var response = RemoveLeakedPromptSections(rawResponse)
                .Replace("User:", "")
                .Replace($"{user}:", "")
                .Replace($"{_assistantName}:", "")
                .Replace("Note:", "")
                .Replace("Limit:", "")
                .Replace("Answer:", "");

            response = Regex.Replace(response, @"^[^\w\d]+", "");
            response = Regex.Replace(response, @"\s+([.,!?;:])", "$1");
            response = Regex.Replace(response, @"\s+", " ").Trim();

            response = response.Replace($"{user}:", "")
                               .Replace($"{_assistantName}:", "")
                               .Replace("Response:", "")
                               .Replace("Prompt:", "");

            response = CollapseRepeatedParagraphs(response);
            response = CollapseRepeatedLines(response);
            response = CollapseNearDuplicateClauses(response);
            response = TrimDanglingFragments(response);

            return Process(user, response.Trim());
        }

        public bool IsIncomplete(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return true;
            }

            var trimmed = response.Trim();
            if (trimmed.Length < 15)
            {
                return true;
            }

            if (!Regex.IsMatch(trimmed, @"[.!?]$"))
            {
                return true;
            }

            if (Regex.IsMatch(trimmed, @"\b(and|or|with|for|to|of|in)\s*$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (trimmed.EndsWith(",") || trimmed.EndsWith(":"))
            {
                return true;
            }

            return trimmed.Contains("...");
        }

        private static string CollapseRepeatedParagraphs(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(response.Trim(), @"\s+", " ");
            var sentences = Regex.Split(normalized, @"(?<=[.!?])\s+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            var uniqueSentences = new List<string>();
            foreach (var sentence in sentences)
            {
                if (uniqueSentences.Any(existing => string.Equals(existing, sentence, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                uniqueSentences.Add(sentence);
            }

            return string.Join(" ", uniqueSentences).Trim();
        }

        private static string RemoveLeakedPromptSections(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var cleaned = response;
            var markers = new[]
            {
                "Start with the direct answer",
                "Give the answer first",
                "Keep the answer short",
                "Answer at a high level first",
                "Use this context when",
                "The app should behave like a documentation copilot",
                "Rules:",
                "User question:",
                "Original user question:",
                "Partial assistant answer:",
                "Answer so far:",
                "Missing continuation only:",
                "Missing final part only:",
                "Incomplete answer to fix:",
                "Final answer:"
            };

            foreach (var marker in markers)
            {
                var index = cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    cleaned = cleaned[..index];
                }
            }

            return cleaned;
        }

        private static string CollapseRepeatedLines(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(response, @"\s+", " ").Trim();
            var clauses = Regex.Split(normalized, @"(?<=[.!?])\s+|(?<=:)\s+|(?<=-)\s+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            var unique = new List<string>();
            foreach (var clause in clauses)
            {
                if (unique.Any(existing => existing.Equals(clause, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                unique.Add(clause);
            }

            return string.Join(" ", unique).Trim();
        }

        private static string TrimDanglingFragments(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var trimmed = response.Trim();
            trimmed = Regex.Replace(trimmed, @"\s+\b(and|or|with|for|to|of|in)\s*$", "", RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, @"[:;,.\-]+\s*$", match => match.Value.Contains('.') ? "." : "");
            return trimmed.Trim();
        }

        private static string CollapseNearDuplicateClauses(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var clauses = Regex.Split(response.Trim(), @"(?<=[.!?])\s+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            var filtered = new List<string>();
            foreach (var clause in clauses)
            {
                if (filtered.Any(existing => AreNearDuplicates(existing, clause)))
                {
                    continue;
                }

                filtered.Add(clause);
            }

            return string.Join(" ", filtered).Trim();
        }

        private static bool AreNearDuplicates(string left, string right)
        {
            var normalizedLeft = Regex.Replace(left.ToLowerInvariant(), @"[^\w\s]", " ").Trim();
            var normalizedRight = Regex.Replace(right.ToLowerInvariant(), @"[^\w\s]", " ").Trim();

            if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var leftWords = normalizedLeft
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rightWords = normalizedRight
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (leftWords.Length < 5 || rightWords.Length < 5)
            {
                return false;
            }

            var leftPrefix = string.Join(' ', leftWords.Take(6));
            var rightPrefix = string.Join(' ', rightWords.Take(6));
            if (string.Equals(leftPrefix, rightPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var leftSet = leftWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var overlap = rightWords.Count(word => leftSet.Contains(word));
            return overlap >= Math.Min(leftWords.Length, rightWords.Length) * 0.75;
        }
    }
}
