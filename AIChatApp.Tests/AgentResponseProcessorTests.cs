using AIChatApp.API.Services.ChatApp.Processing;
using AIChatApp.Core.Config;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIChatApp.Tests;

public class AgentResponseProcessorTests
{
    [Fact]
    public void Clean_ShouldRemoveLeakedPromptSections_AndCollapseRepetition()
    {
        var processor = CreateProcessor();

        var raw = "The gateway protects the API by checking API keys. Use this context when the app should behave like a documentation copilot for the AIChatApp repository. The gateway protects the API by checking API keys.";

        var cleaned = processor.Clean(raw, "testUser");

        Assert.Equal("The gateway protects the API by checking API keys.", cleaned);
    }

    [Fact]
    public void Clean_ShouldKeepShortCompleteAnswer_WhenAlreadyClean()
    {
        var processor = CreateProcessor();

        var cleaned = processor.Clean("The browser conversation workspace is stored in localStorage so it can be restored after refresh or reopen.", "testUser");

        Assert.Equal("The browser conversation workspace is stored in localStorage so it can be restored after refresh or reopen.", cleaned);
    }

    private static AgentResponseProcessor CreateProcessor()
    {
        var options = Options.Create(new AssistantProfileOptions
        {
            AssistantName = "AI Assistant"
        });

        return new AgentResponseProcessor(options);
    }
}
