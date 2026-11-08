using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Application.Recharges;

public interface IRechargeRepository
{
    /// <summary>Inserts the row. Afterwards transaction.TransactionId holds the database-generated value.</summary>
    Task AddAsync(RechargeTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves changes made to a transaction that was loaded or added through this repository.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<RechargeTransaction?> FindByIdAsync(int id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<RechargeTransaction> Items, int TotalCount)> GetHistoryAsync(
        string mobileNumber, int page, int pageSize, CancellationToken cancellationToken);
}
