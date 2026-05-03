using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using AIChatApp.MLTraining.Config;
using AIChatApp.MLTraining.Models;
using Microsoft.Extensions.Options;

namespace AIChatApp.MLTraining.Services;

public sealed class TrainingAuthService(HttpClient httpClient, IOptions<TrainingFrontendOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TrainingFrontendOptions _options = options.Value;

    public async Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        using var request = CreateRequest(HttpMethod.Get, "auth/2fa/status", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<string> LoginAsync(TrainingLoginPayload payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/login");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadError(content, "Unable to sign in to ML Training."));
        }

        var loginResponse = JsonSerializer.Deserialize<TrainingLoginResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Login succeeded but no token was returned.");

        if (string.IsNullOrWhiteSpace(loginResponse.Token))
        {
            throw new InvalidOperationException("Login succeeded but the token was empty.");
        }

        return loginResponse.Token;
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

    private static string ReadError(string content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? fallback;
            }

            if (document.RootElement.TryGetProperty("title", out var title))
            {
                return title.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
            return content.Trim('"');
        }

        return fallback;
    }
}
