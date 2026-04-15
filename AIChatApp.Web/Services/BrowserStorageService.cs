using Microsoft.JSInterop;
using System.Text.Json;

namespace AIChatApp.Web.Services;

public class BrowserStorageService(IJSRuntime jsRuntime)
{
    public ValueTask<string?> GetAsync(string key)
        => jsRuntime.InvokeAsync<string?>("aiChatStorage.get", key);

    public ValueTask SetAsync(string key, string value)
        => jsRuntime.InvokeVoidAsync("aiChatStorage.set", key, value);

    public ValueTask RemoveAsync(string key)
        => jsRuntime.InvokeVoidAsync("aiChatStorage.remove", key);

    public async ValueTask<T?> GetJsonAsync<T>(string key)
    {
        var raw = await GetAsync(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(raw);
    }

    public ValueTask SetJsonAsync<T>(string key, T value)
        => SetAsync(key, JsonSerializer.Serialize(value));
}
