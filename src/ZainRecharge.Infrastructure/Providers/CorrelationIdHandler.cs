using ZainRecharge.Application.Common;

namespace ZainRecharge.Infrastructure.Providers;

internal sealed class CorrelationIdHandler(ICorrelationIdAccessor correlation) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (correlation.CorrelationId is { Length: > 0 } id)
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", id);

        return base.SendAsync(request, cancellationToken);
    }
}
