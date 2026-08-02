using AIChatApp.Core.Config;

namespace AIChatApp.API.Services.ChatApp.Content
{
    /// <summary>
    /// Defines the profile-scoped source for prompts and structured assistant knowledge.
    /// Implementations must preserve published database content as the live source and bundled files as fallback data.
    /// </summary>
    public interface IAssistantContentService
    {
        Task<string> LoadPromptAsync(string profileId, string templateName, CancellationToken cancellationToken = default);
        Task<string> LoadKnowledgeTextAsync(string profileId, string sourceName, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<JsonQuickAnswerEntry>> LoadQuickAnswersAsync(string profileId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<JsonTopicEntry>> LoadTopicsAsync(string profileId, CancellationToken cancellationToken = default);
        Task SeedProfileContentAsync(string profileId, CancellationToken cancellationToken = default);
        void InvalidateProfileCache(string profileId);
    }
}
