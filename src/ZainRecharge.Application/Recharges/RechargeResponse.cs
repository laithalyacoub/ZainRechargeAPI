namespace ZainRecharge.Application.Recharges;

public sealed record RechargeResponse(
    int Id,
    string TransactionId,
    string MobileNumber,
    decimal Amount,
    string Status,
    string? PaymentReference,
    string? FailureReason,
    string Message,
    DateTime CreatedAt,
    DateTime UpdatedAt);
