namespace Clawbot.Api.Middleware;

/// <summary>
/// Emits browser-facing security headers. HSTS is intentionally deferred to the
/// TLS-terminating gateway/reverse proxy because ASP.NET hosts behind it on HTTP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly (string Name, string Value)[] Headers =
    [
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Referrer-Policy", "strict-origin-when-cross-origin"),
        ("Content-Security-Policy", "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; img-src 'self' data: blob: https:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self' https: wss:;"),
    ];

    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var (name, value) in Headers)
        {
            if (!context.Response.Headers.ContainsKey(name))
                context.Response.Headers[name] = value;
        }

        await _next(context).ConfigureAwait(false);
    }
}