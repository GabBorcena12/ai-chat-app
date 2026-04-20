using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace AIChatApp.Core.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Request was canceled or timed out.");

                if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted)
                {
                    return;
                }

                await WriteErrorResponse(
                    context,
                    HttpStatusCode.RequestTimeout,
                    "The request timed out or was canceled."
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "Unhandled exception.");

                await WriteErrorResponse(
                    context,
                    HttpStatusCode.InternalServerError,
                    ex.Message
                );
            }
        }

        private static async Task WriteErrorResponse(
            HttpContext context,
            HttpStatusCode statusCode,
            string errorMessage)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var response = new
            {
                StatusCode = (int)statusCode,
                ErrorMessage = errorMessage
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
