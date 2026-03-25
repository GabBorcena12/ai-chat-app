namespace AIChatApp.Gateway.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Log request
            _logger.LogInformation("Incoming Request: {method} {path}",
                context.Request.Method, context.Request.Path);

            // Copy response body to read it
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context); // Call the next middleware

            // Log response
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            _logger.LogInformation("⬅️ Response: {statusCode} {body}",
                context.Response.StatusCode, text);

            await responseBody.CopyToAsync(originalBodyStream);
        }
    }
}