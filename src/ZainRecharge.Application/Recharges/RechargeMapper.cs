using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Application.Recharges;

public static class RechargeMapper
{
    public static RechargeResponse ToResponse(RechargeTransaction t) => new(
        t.Id,
        t.TransactionId,
        t.MobileNumber,
        t.Amount,
        t.Status.ToString().ToUpperInvariant(),
        t.PaymentReference,
        t.FailureReason,
        MessageFor(t),
        t.CreatedAt,
        t.UpdatedAt);

    private static string MessageFor(RechargeTransaction t) => t.Status switch
    {
        RechargeStatus.Success => "Recharge completed successfully",
        RechargeStatus.Failed => t.FailureReason ?? "Recharge failed",
        RechargeStatus.Unknown => "The payment result is not known yet. Check the status again later.",
        _ => "Recharge is being processed"
    };
}
