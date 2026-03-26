using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace AIChatApp.Core.Middleware
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;

        // Store request timestamps per IP address
        private static readonly ConcurrentDictionary<string, List<DateTime>> _requests = new();

        private readonly int _limit;
        private readonly TimeSpan _window;

        public RateLimitingMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _limit = config.GetValue<int>("RateLimiting:Limit");

            var windowSeconds = config.GetValue<int>("RateLimiting:WindowSeconds");
            _window = TimeSpan.FromSeconds(windowSeconds);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Get client IP address
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var now = DateTime.UtcNow;

            // Get or create request list for this IP
            var requestTimes = _requests.GetOrAdd(ip, _ => new List<DateTime>());

            lock (requestTimes)
            {
                // Remove requests older than the defined time window
                requestTimes.RemoveAll(t => (now - t) > _window);

                // Check if request count exceeds the limit
                if (requestTimes.Count >= _limit)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    // Inform client when to retry
                    context.Response.Headers["Retry-After"] = _window.TotalSeconds.ToString();
                    context.Response.WriteAsync("Too many requests. Please try again later.");
                    return;
                }
                
                // Add current request timestamp
                requestTimes.Add(now);
            }

            // Continue to next middleware if within limit
            await _next(context);
        }
    }
}