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
            _logger.LogInformation("Incoming Request: {method} {path}",
                context.Request.Method, context.Request.Path);

            var originalBodyStream = context.Response.Body;
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            try
            {
                await _next(context);

                // Read and log response
                responseBody.Seek(0, SeekOrigin.Begin);
                var text = await new StreamReader(responseBody).ReadToEndAsync();
                responseBody.Seek(0, SeekOrigin.Begin);

                _logger.LogInformation("⬅️ Response: {statusCode} {body}",
                    context.Response.StatusCode, text);

                // Copy back to original
                await responseBody.CopyToAsync(originalBodyStream);
            }
            finally
            {
                // Ensure Response.Body is restored so error handlers can write to the real stream
                context.Response.Body = originalBodyStream;
                responseBody.Dispose();
            }
        }
    }
}