namespace ZainRecharge.Application.Recharges;

public enum RechargeOutcome
{
    Completed,           // 201
    Declined,            // 200
    Unknown,             // 202
    ProviderUnavailable, // 503
    ProviderError        // 502
}

public sealed record RechargeResult(RechargeOutcome Outcome, RechargeResponse Recharge);
