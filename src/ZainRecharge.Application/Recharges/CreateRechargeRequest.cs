namespace ZainRecharge.Application.Recharges;

public sealed record CreateRechargeRequest(string? MobileNumber, decimal? Amount);
