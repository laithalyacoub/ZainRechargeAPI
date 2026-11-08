using ZainRecharge.Application.Common;

namespace ZainRecharge.Api.Middleware;

public sealed class HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    : ICorrelationIdAccessor
{
    public string? CorrelationId =>
        httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.HeaderName] as string;
}
