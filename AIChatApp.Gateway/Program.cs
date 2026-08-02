
using AIChatApp.Core.Middleware;

namespace AIChatApp.Gateway
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var allowLocalCredentials = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Docker");
            var configuredApiKeys = builder.Configuration.GetSection("ApiKey.Settings:Keys").Get<string[]>() ?? [];
            if (configuredApiKeys.Length == 0 || (!allowLocalCredentials && configuredApiKeys.Any(IsPlaceholderSecret)))
            {
                throw new InvalidOperationException("Configure at least one non-placeholder API key in ApiKey.Settings:Keys.");
            }

            // Add YARP
            builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));


            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Optional: HTTPS redirection
            app.UseHttpsRedirection();

            // Gateway Middleware
            app.UseMiddleware<ApiKeyMiddleware>();
            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseMiddleware<RateLimitingMiddleware>();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            // Map YARP routes
            app.MapReverseProxy();

            // Map controllers if you have extra endpoints not covered by YARP
            app.MapControllers();

            app.Run();
        }

        private static bool IsPlaceholderSecret(string value)
            => string.IsNullOrWhiteSpace(value)
               || value.Contains("dummy", StringComparison.OrdinalIgnoreCase)
               || value.Contains("change-before", StringComparison.OrdinalIgnoreCase)
               || value.Contains("change-for-real", StringComparison.OrdinalIgnoreCase);
    }
}
