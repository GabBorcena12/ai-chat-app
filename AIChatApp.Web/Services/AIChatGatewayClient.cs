using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using AIChatApp.Web.Config;
using AIChatApp.Web.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace AIChatApp.Web.Services;

public class AIChatGatewayClient(
    HttpClient httpClient,
    IOptions<FrontendOptions> options,
    ILogger<AIChatGatewayClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FrontendOptions _options = options.Value;

    public async Task<string> RegisterAsync(RegisterPayload payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/register");
        request.Content = CreateJsonContent(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadError(content, "Unable to register right now."));
        }

        return string.IsNullOrWhiteSpace(content) ? "User registered successfully." : content.Trim('"');
    }

    public async Task<string> LoginAsync(LoginPayload payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/login");
        request.Content = CreateJsonContent(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadError(content, "Unable to log in."));
        }

        var loginResponse = JsonSerializer.Deserialize<LoginResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Login succeeded but no token was returned.");

        return loginResponse.Token;
    }

    public async Task<TwoFactorSetupResponse> SetupTwoFactorAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/2fa/setup", token);
        request.Content = CreateJsonContent(new { });
        logger.LogInformation("2FA setup request -> {Uri}", request.RequestUri);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        logger.LogInformation("2FA setup response <- {StatusCode}", (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("2FA setup failed. Body: {Body}", string.IsNullOrWhiteSpace(content) ? "<empty>" : content);
            throw new InvalidOperationException(ReadError(content, "Unable to start 2FA setup."));
        }

        return JsonSerializer.Deserialize<TwoFactorSetupResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("2FA setup response was empty.");
    }

    public async Task<bool> GetTwoFactorStatusAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "auth/2fa/status", token);
        logger.LogInformation("2FA status request -> {Uri}", request.RequestUri);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        logger.LogInformation("2FA status response <- {StatusCode}", (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("2FA status failed. Body: {Body}", string.IsNullOrWhiteSpace(content) ? "<empty>" : content);
            throw new InvalidOperationException(ReadError(content, "Unable to load 2FA status."));
        }

        var status = JsonSerializer.Deserialize<TwoFactorStatusResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("2FA status response was empty.");

        return status.IsEnabled;
    }

    public async Task<string> VerifyTwoFactorAsync(string token, string code, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/2fa/verify", token);
        request.Content = CreateJsonContent(new VerifyTwoFactorPayload { Code = code });
        logger.LogInformation("2FA verify request -> {Uri}", request.RequestUri);

        return await SendTextAsync(request, cancellationToken, "Unable to verify the authenticator code.");
    }

    public async Task<string> DisableTwoFactorAsync(string token, string code, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/2fa/disable", token);
        request.Content = CreateJsonContent(new VerifyTwoFactorPayload { Code = code });

        return await SendTextAsync(request, cancellationToken, "Unable to disable 2FA.");
    }

    public async IAsyncEnumerable<StreamEvent> StreamChatAsync(
        string token,
        ChatRequestPayload payload,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "chat/ask-stream", token);
        request.Content = CreateJsonContent(payload);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Chat stream unauthorized. Body: {Body}", string.IsNullOrWhiteSpace(error) ? "<empty>" : error);
                throw new InvalidOperationException("Your session is no longer valid for chat requests. Please sign in again.");
            }
            throw new InvalidOperationException(ReadError(error, "Unable to stream a response."));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string eventName = string.Empty;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var json = line["data:".Length..].Trim();
                var content = ExtractContent(json);
                yield return new StreamEvent
                {
                    EventName = eventName,
                    Content = content
                };
            }
        }
    }

    public async Task<ChatResponsePayload> ContinueAsync(
        string token,
        ContinueChatRequestPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "chat/ask-continue", token);
        request.Content = CreateJsonContent(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Chat continuation unauthorized. Body: {Body}", string.IsNullOrWhiteSpace(content) ? "<empty>" : content);
                throw new InvalidOperationException("Your session is no longer valid for chat requests. Please sign in again.");
            }
            throw new InvalidOperationException(ReadError(content, "Unable to continue the response."));
        }

        return JsonSerializer.Deserialize<ChatResponsePayload>(content, JsonOptions)
            ?? throw new InvalidOperationException("The continuation response was empty.");
    }

    public async Task<string> ReportResponseAsync(
        string token,
        ReportChatResponsePayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "chat/report-response", token);
        request.Content = CreateJsonContent(payload);

        return await SendTextAsync(request, cancellationToken, "Unable to save the response report.");
    }

    public async Task<List<ChatConversationHistoryViewModel>> GetChatConversationsAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "ChatHistory/conversations", token);
        return await SendJsonAsync<List<ChatConversationHistoryViewModel>>(request, cancellationToken, "Unable to load chat history.")
            ?? [];
    }

    public async Task<string> UpdateChatConversationTitleAsync(
        string token,
        string chatId,
        string title,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"ChatHistory/conversations/{Uri.EscapeDataString(chatId)}/title", token);
        request.Content = CreateJsonContent(new { title });
        return await SendTextAsync(request, cancellationToken, "Unable to rename the conversation.");
    }

    public async Task<string> DeleteChatConversationAsync(
        string token,
        string chatId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"ChatHistory/conversations/{Uri.EscapeDataString(chatId)}", token);
        return await SendTextAsync(request, cancellationToken, "Unable to delete the conversation.");
    }

    public async Task<List<BackofficeReportViewModel>> GetReportedResponsesAsync(
        string token,
        string? status,
        CancellationToken cancellationToken)
    {
        var suffix = string.IsNullOrWhiteSpace(status) ? string.Empty : $"?status={Uri.EscapeDataString(status)}";
        using var request = CreateRequest(HttpMethod.Get, $"backoffice/reports{suffix}", token);
        return await SendJsonAsync<List<BackofficeReportViewModel>>(request, cancellationToken, "Unable to load reported responses.")
            ?? [];
    }

    public async Task<BackofficeWorkflowSummaryViewModel> GetBackofficeWorkflowSummaryAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "backoffice/workflow-summary", token);
        return await SendJsonAsync<BackofficeWorkflowSummaryViewModel>(request, cancellationToken, "Unable to load the backoffice workflow summary.")
            ?? new BackofficeWorkflowSummaryViewModel();
    }

    public async Task<List<TrainingCandidateViewModel>> GetTrainingCandidatesAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "backoffice/training-candidates", token);
        return await SendJsonAsync<List<TrainingCandidateViewModel>>(request, cancellationToken, "Unable to load training candidates.")
            ?? [];
    }

    public async Task<ReviewerWorkflowStateViewModel> GetReviewerWorkflowStateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "backoffice/reviewer/state", token);
        return await SendJsonAsync<ReviewerWorkflowStateViewModel>(request, cancellationToken, "Unable to load reviewer workflow state.")
            ?? new ReviewerWorkflowStateViewModel();
    }

    public async Task<string> BuildReviewerDatasetAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "backoffice/reviewer/build-dataset", token);
        request.Content = CreateJsonContent(new { });
        return await SendTextAsync(request, cancellationToken, "Unable to build the reviewer dataset.");
    }

    public async Task<string> TrainReviewerAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "backoffice/reviewer/train", token);
        request.Content = CreateJsonContent(new { });
        return await SendTextAsync(request, cancellationToken, "Unable to train the reviewer model.");
    }

    public async Task<string> PublishLatestReviewerAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "backoffice/reviewer/publish-latest", token);
        request.Content = CreateJsonContent(new { });
        return await SendTextAsync(request, cancellationToken, "Unable to publish the reviewer model.");
    }

    public async Task<string> ReviewReportedResponseAsync(
        string token,
        int reportId,
        BackofficeReviewPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"backoffice/reports/{reportId}/review", token);
        request.Content = CreateJsonContent(payload);
        return await SendTextAsync(request, cancellationToken, "Unable to save the report review.");
    }

    public async Task<List<PromptTemplateViewModel>> GetPromptTemplatesAsync(
        string token,
        string profileId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"backoffice/prompt-templates?profileId={Uri.EscapeDataString(profileId)}", token);
        return await SendJsonAsync<List<PromptTemplateViewModel>>(request, cancellationToken, "Unable to load prompt templates.")
            ?? [];
    }

    public async Task<string> UpdatePromptTemplateAsync(
        string token,
        int id,
        SavePromptTemplatePayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"backoffice/prompt-templates/{id}", token);
        request.Content = CreateJsonContent(payload);
        return await SendTextAsync(request, cancellationToken, "Unable to update the prompt template.");
    }

    public async Task<List<KnowledgeEntryViewModel>> GetKnowledgeEntriesAsync(
        string token,
        string profileId,
        string? entryType,
        CancellationToken cancellationToken)
    {
        var path = new StringBuilder($"backoffice/knowledge?profileId={Uri.EscapeDataString(profileId)}");
        if (!string.IsNullOrWhiteSpace(entryType))
        {
            path.Append($"&entryType={Uri.EscapeDataString(entryType)}");
        }

        using var request = CreateRequest(HttpMethod.Get, path.ToString(), token);
        return await SendJsonAsync<List<KnowledgeEntryViewModel>>(request, cancellationToken, "Unable to load knowledge entries.")
            ?? [];
    }

    public async Task<List<BackofficeUserViewModel>> GetBackofficeUsersAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "backoffice/users", token);
        return await SendJsonAsync<List<BackofficeUserViewModel>>(request, cancellationToken, "Unable to load users.")
            ?? [];
    }

    public async Task<string> CreateBackofficeUserAsync(
        string token,
        SaveBackofficeUserPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "backoffice/users", token);
        request.Content = CreateJsonContent(payload);
        return await SendTextAsync(request, cancellationToken, "Unable to create the user.");
    }

    public async Task<string> UpdateBackofficeUserAsync(
        string token,
        string userId,
        SaveBackofficeUserPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"backoffice/users/{Uri.EscapeDataString(userId)}", token);
        request.Content = CreateJsonContent(new
        {
            payload.Email,
            payload.Roles,
            payload.IsConfirmed,
            payload.IsDisabled
        });
        return await SendTextAsync(request, cancellationToken, "Unable to update the user.");
    }

    public async Task<SaveKnowledgeEntryResult> CreateKnowledgeEntryWithResultAsync(
        string token,
        SaveKnowledgeEntryPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "backoffice/knowledge", token);
        request.Content = CreateJsonContent(payload);
        return await SendJsonAsync<SaveKnowledgeEntryResult>(request, cancellationToken, "Unable to create the knowledge entry.")
            ?? new SaveKnowledgeEntryResult { Message = "Knowledge entry created." };
    }

    public async Task<string> UpdateKnowledgeEntryAsync(
        string token,
        int id,
        SaveKnowledgeEntryPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"backoffice/knowledge/{id}", token);
        request.Content = CreateJsonContent(payload);
        return await SendTextAsync(request, cancellationToken, "Unable to update the knowledge entry.");
    }

    public async Task<string> LinkReportToKnowledgeEntryAsync(
        string token,
        int reportId,
        int knowledgeEntryId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"backoffice/reports/{reportId}/promoted-knowledge/{knowledgeEntryId}", token);
        request.Content = CreateJsonContent(new { });
        return await SendTextAsync(request, cancellationToken, "Unable to link the report to the knowledge entry.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? token = null)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Add("X-Api-Client", _options.ApiClientName);
        request.Headers.Add("X-Api-Key", _options.ApiKey);

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private Uri BuildUri(string path)
        => new(new Uri(_options.GatewayBaseUrl), path);

    private static StringContent CreateJsonContent<T>(T payload)
        => new(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

    private async Task<string> SendTextAsync(HttpRequestMessage request, CancellationToken cancellationToken, string fallbackError)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        logger.LogInformation("{Method} {Uri} <- {StatusCode}", request.Method, request.RequestUri, (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Request failed for {Uri}. Body: {Body}", request.RequestUri, string.IsNullOrWhiteSpace(content) ? "<empty>" : content);
            throw new InvalidOperationException(ReadError(content, fallbackError));
        }

        return string.IsNullOrWhiteSpace(content) ? "Success." : content.Trim('"');
    }

    private async Task<T?> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken, string fallbackError)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        logger.LogInformation("{Method} {Uri} <- {StatusCode}", request.Method, request.RequestUri, (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Request failed for {Uri}. Body: {Body}", request.RequestUri, string.IsNullOrWhiteSpace(content) ? "<empty>" : content);
            throw new InvalidOperationException(ReadError(content, fallbackError));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static string ExtractContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("content", out var contentProperty))
        {
            return contentProperty.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ReadError(string content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
        }

        return content;
    }
}
