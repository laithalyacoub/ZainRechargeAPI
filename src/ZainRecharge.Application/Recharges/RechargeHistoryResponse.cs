namespace ZainRecharge.Application.Recharges;

public sealed record RechargeHistoryResponse(
    string MobileNumber,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<RechargeResponse> Items);
