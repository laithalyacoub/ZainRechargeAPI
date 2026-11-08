namespace ZainRecharge.Application.Recharges;

public interface IRechargeService
{
    Task<RechargeResult> CreateAsync(CreateRechargeRequest request, CancellationToken cancellationToken);
    Task<RechargeResponse> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<RechargeHistoryResponse> GetHistoryAsync(string? mobileNumber, int page, int pageSize, CancellationToken cancellationToken);
}
