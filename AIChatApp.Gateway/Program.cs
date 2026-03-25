
using AIChatApp.Gateway.Middleware;

namespace AIChatApp.Gateway
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
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
    }
}
