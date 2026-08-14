using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace WebApi.Auth;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthOptions _options;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        foreach (var prefix in _options.AnonymousPathPrefixes ?? [])
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Auth is enabled but ApiKey is not configured." });
            return;
        }

        if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var provided) ||
            string.IsNullOrWhiteSpace(provided))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = $"Missing {_options.HeaderName} header." });
            return;
        }

        if (!FixedTimeEquals(_options.ApiKey, provided.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        await _next(context);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        if (a.Length != b.Length)
        {
            // still run a comparison to reduce trivial timing leaks on length
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
