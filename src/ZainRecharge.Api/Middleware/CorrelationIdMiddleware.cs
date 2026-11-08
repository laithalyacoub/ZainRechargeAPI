using Serilog.Context;

namespace ZainRecharge.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context);

        context.Items[HeaderName] = correlationId; // for code that needs it later in this request
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId; // the caller gets it back
            return Task.CompletedTask;
        });

        // Every log line written while this request runs now carries CorrelationId.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ReadOrCreate(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        return IsSafe(incoming) ? incoming : Guid.NewGuid().ToString("N");
    }

    // A value from outside goes into our logs, so only short and harmless values are accepted.
    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
