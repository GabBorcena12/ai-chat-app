using AIChatApp.Core.Config;
using AIChatApp.Web.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AIChatApp.Web.Services;

public sealed class FaqContentService
{
    private const string ProfileId = "Documentation";
    private static readonly TimeSpan FaqCacheDuration = TimeSpan.FromMinutes(15);
    private readonly ChatPaths _paths = new();
    private readonly IMemoryCache _cache;

    public FaqContentService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public IReadOnlyList<FaqItemViewModel> LoadQuickAnswers()
        => _cache.GetOrCreate("faq-content:quick-answers", cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = FaqCacheDuration;
            return _paths.LoadAssistantQuickAnswers(ProfileId)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Answer) && entry.Aliases.Count > 0)
            .Select(entry => new FaqItemViewModel
            {
                Question = NormalizeQuestion(entry.Aliases[0]),
                Answer = entry.Answer,
                Category = GuessCategory(entry.Aliases[0], entry.Answer)
            })
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Question)
            .ToList();
        }) ?? [];

    public IReadOnlyList<FaqTopicViewModel> LoadTopics()
        => _cache.GetOrCreate("faq-content:topics", cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = FaqCacheDuration;
            return _paths.LoadAssistantTopics(ProfileId)
            .Where(topic => !string.IsNullOrWhiteSpace(topic.Topic) && !string.IsNullOrWhiteSpace(topic.Summary))
            .Select(topic => new FaqTopicViewModel
            {
                Topic = ToTitle(topic.Topic),
                Summary = topic.Summary,
                Keywords = topic.Keywords.Take(6).ToList()
            })
            .OrderBy(topic => topic.Topic)
            .ToList();
        }) ?? [];

    private static string NormalizeQuestion(string value)
    {
        var question = value.Trim();
        if (question.Length == 0)
        {
            return "Question";
        }

        question = char.ToUpperInvariant(question[0]) + question[1..];
        return question.EndsWith("?") ? question : $"{question}?";
    }

    private static string GuessCategory(string question, string answer)
    {
        var combined = $"{question} {answer}".ToLowerInvariant();

        if (combined.Contains("2fa") || combined.Contains("auth") || combined.Contains("token") || combined.Contains("gateway header"))
        {
            return "Auth";
        }

        if (combined.Contains("model") || combined.Contains("gguf") || combined.Contains("localmodel"))
        {
            return "Model";
        }

        if (combined.Contains("stream") || combined.Contains("continue") || combined.Contains("chat"))
        {
            return "Chat";
        }

        if (combined.Contains("docker") || combined.Contains("sql") || combined.Contains("config") || combined.Contains("gatewaybaseurl"))
        {
            return "Setup";
        }

        return "Project";
    }

    private static string ToTitle(string value)
        => string.Join(' ', value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
