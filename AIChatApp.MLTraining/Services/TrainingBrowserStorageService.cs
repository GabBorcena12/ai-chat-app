using Microsoft.JSInterop;

namespace AIChatApp.MLTraining.Services;

public sealed class TrainingBrowserStorageService(IJSRuntime jsRuntime)
{
    public ValueTask<string?> GetAsync(string key)
        => jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask SetAsync(string key, string value)
        => jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);

    public ValueTask RemoveAsync(string key)
        => jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
}
