using Microsoft.EntityFrameworkCore;
using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Infrastructure.Persistence;

public class ZainDbContext(DbContextOptions<ZainDbContext> options) : DbContext(options)
{
    public DbSet<RechargeTransaction> RechargeTransactions => Set<RechargeTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ZainDbContext).Assembly);
}
