using AIChatApp.Core.Config;

namespace AIChatApp.API.Services.Content
{
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
