using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AIChatApp.Core.Middleware
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string[] _apiKeys;
        private readonly string _apiClientName;

        public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _apiKeys = config.GetSection("ApiKey.Settings:Keys").Get<string[]>() ?? new string[0];
            _apiClientName = config.GetValue<string>("ApiKey.Settings:ClientName") ?? "UnknownClient";
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Validate API key only if header exists (optional)
            context.Request.Headers.TryGetValue("X-Api-Key", out var extractedKey);
            context.Request.Headers.TryGetValue("X-Api-Client", out var extractedClient);

            if (!string.IsNullOrWhiteSpace(extractedKey) 
                && !string.IsNullOrWhiteSpace(extractedClient))
            {
                if (_apiKeys.Any(key => string.Equals(key, extractedKey.ToString(), StringComparison.OrdinalIgnoreCase))
                    && string.Equals(_apiClientName, extractedClient, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }
            }
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid API Key");
            return;

        }
    }
}
