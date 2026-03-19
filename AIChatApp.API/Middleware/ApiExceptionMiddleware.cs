using System.Net;
using System.Text.Json;

namespace AIChatApp.API.Middleware
{   

    public class ApiExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiExceptionMiddleware> _logger;

        public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context); // Continue to the next middleware/controller
            }
            catch (OperationCanceledException oce)
            {
                _logger.LogWarning(oce, "Request was canceled or timed out.");

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)HttpStatusCode.RequestTimeout;

                var response = new
                {
                    Prompt = context.Request.HasFormContentType
                             ? context.Request.Form["Prompt"].ToString()
                             : string.Empty,
                    Response = "The request timed out or was canceled."
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception.");

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

                var response = new
                {
                    Prompt = context.Request.HasFormContentType
                             ? context.Request.Form["Prompt"].ToString()
                             : string.Empty,
                    Response = "An internal server error occurred."
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
        }
    }
}
