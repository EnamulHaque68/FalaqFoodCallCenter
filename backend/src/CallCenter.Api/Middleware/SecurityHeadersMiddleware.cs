namespace CallCenter.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers.Append("X-Content-Type-Options", "nosniff");
            }

            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers.Append("X-Frame-Options", "DENY");
            }

            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            }

            if (!headers.ContainsKey("X-XSS-Protection"))
            {
                headers.Append("X-XSS-Protection", "1; mode=block");
            }

            if (!headers.ContainsKey("Permissions-Policy"))
            {
                headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            }

            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                if (context.Request.Path.StartsWithSegments("/swagger"))
                {
                    headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; frame-ancestors 'none';");
                }
                else
                {
                    headers.Append("Content-Security-Policy", "default-src 'self'; frame-ancestors 'none';");
                }
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
