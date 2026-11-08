namespace ZainRecharge.Application.Providers;

public sealed record ProviderPaymentRequest(string TransactionId, string MobileNumber, decimal Amount);

public enum ProviderOutcome
{
    Success,          // provider said SUCCESS
    Declined,         // provider said FAILED (for example amount above 20)
    Unavailable,      // provider answered 503, or we could not reach it, or we never got a token: payment was NOT attempted
    Rejected,         // provider answered 400 or 401: payment was NOT attempted
    ServerError,      // provider answered 500 or an unreadable 200: we cannot know
    TimedOut,         // no answer within the time limit: we cannot know
    NetworkError      // connection broke during the call: we cannot know
}

public sealed record ProviderResult(ProviderOutcome Outcome, string? PaymentReference = null, string? Message = null);

public interface IPaymentProviderClient
{
    Task<ProviderResult> ProcessPaymentAsync(ProviderPaymentRequest request, CancellationToken cancellationToken);
}
