using ZainRecharge.Application.Providers;
using ZainRecharge.Application.Recharges;
using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

// Behaves like the database: hands out ids and builds TXN-1001, TXN-1002 ...
internal sealed class FakeRechargeRepository : IRechargeRepository
{
    private int _nextId = 1;

    public List<RechargeTransaction> Saved { get; } = [];

    public Task AddAsync(RechargeTransaction transaction, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        Set(transaction, nameof(RechargeTransaction.Id), id);
        Set(transaction, nameof(RechargeTransaction.TransactionId), $"TXN-{1000 + id}");
        Saved.Add(transaction);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<RechargeTransaction?> FindByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.FirstOrDefault(t => t.Id == id));

    public Task<(IReadOnlyList<RechargeTransaction> Items, int TotalCount)> GetHistoryAsync(
        string mobileNumber, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = Saved
            .Where(t => t.MobileNumber == mobileNumber)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .ToList();

        IReadOnlyList<RechargeTransaction> pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((pageItems, all.Count));
    }

    // The entity has private setters, so the fake plays the role of the database by reflection.
    private static void Set(object target, string property, object value) =>
        typeof(RechargeTransaction).GetProperty(property)!.SetValue(target, value);
}

internal sealed class FakeProviderClient(ProviderResult result) : IPaymentProviderClient
{
    public List<ProviderPaymentRequest> Calls { get; } = [];

    public Task<ProviderResult> ProcessPaymentAsync(
        ProviderPaymentRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(result);
    }
}
