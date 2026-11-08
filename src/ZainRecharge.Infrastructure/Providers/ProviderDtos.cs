namespace ZainRecharge.Infrastructure.Providers;

internal sealed record ProviderTokenBody(string? AccessToken, int ExpiresInSeconds);

internal sealed record ProviderPaymentBody(
    string? TransactionId, string? PaymentReference, string? Status, string? Message);
