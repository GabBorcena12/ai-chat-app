using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AIChatApp.Core.Middleware
{
    public class IpWhitelistMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string[] _whitelistedIPs;

        public IpWhitelistMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _whitelistedIPs = config.GetSection("ApiSettings:WhitelistedIPs").Get<string[]>() ?? new string[0];
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";

            // Check if allowed
            if (!_whitelistedIPs.Contains(remoteIp) && !IsLocalOrGateway(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync(
                    $"Forbidden: Your IP {remoteIp} is not allowed."
                );
                return;
            }

            await _next(context);
        }

        private bool IsLocalOrGateway(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";

            // Local dev
            if (remoteIp == "127.0.0.1" || remoteIp == "::1")
                return true;

            // Docker internal network (gateway container)
            // check the container app name on docker file usage guide
            var host = context.Request.Headers["Host"].ToString();

            return host.Contains("aichatapp-gateway", StringComparison.OrdinalIgnoreCase);
        }
    }
}