using Microsoft.EntityFrameworkCore;
using ZainRecharge.Application.Recharges;
using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Infrastructure.Persistence;

public sealed class RechargeRepository(ZainDbContext db) : IRechargeRepository
{
    public async Task AddAsync(RechargeTransaction transaction, CancellationToken cancellationToken)
    {
        db.RechargeTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    // The entity is tracked since AddAsync, so EF Core knows exactly what changed.
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public Task<RechargeTransaction?> FindByIdAsync(int id, CancellationToken cancellationToken) =>
        db.RechargeTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<RechargeTransaction> Items, int TotalCount)> GetHistoryAsync(
        string mobileNumber, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.RechargeTransactions
            .AsNoTracking()
            .Where(t => t.MobileNumber == mobileNumber);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
