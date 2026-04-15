using AIChatApp.Core.Config;
using Xunit;

namespace AIChatApp.Tests;

public class KnowledgeFilesTests
{
    [Fact]
    public void DocumentationQuickAnswers_ShouldContainStreamingDifferenceAnswer()
    {
        var paths = new ChatPaths();

        var entries = paths.LoadAssistantQuickAnswers("Documentation");

        var match = entries.FirstOrDefault(entry =>
            entry.Aliases.Any(alias => alias.Contains("ask-continue differ from ask-stream", StringComparison.OrdinalIgnoreCase)));

        Assert.NotNull(match);
        Assert.Equal(
            "ask-continue finishes a cut-off answer, while ask-stream is the live streaming chat endpoint that sends response chunks as they are generated.",
            match!.Answer);
    }

    [Fact]
    public void DocumentationTopics_ShouldContainOperationsAndTroubleshootingSummary()
    {
        var paths = new ChatPaths();

        var topics = paths.LoadAssistantTopics("Documentation");

        var match = topics.FirstOrDefault(topic =>
            string.Equals(topic.Topic, "operations and troubleshooting", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(match);
        Assert.Contains("Docker runs the solution as containers", match!.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsoleDisallowedTopics_ShouldBeLoadedFromJson()
    {
        var paths = new ChatPaths();

        var topics = paths.LoadDisallowedTopics();

        Assert.Contains("politics", topics, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hacking", topics, StringComparer.OrdinalIgnoreCase);
    }
}
