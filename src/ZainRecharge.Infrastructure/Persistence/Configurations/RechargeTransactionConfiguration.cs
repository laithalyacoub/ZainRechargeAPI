using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Infrastructure.Persistence.Configurations;

public class RechargeTransactionConfiguration : IEntityTypeConfiguration<RechargeTransaction>
{
    public void Configure(EntityTypeBuilder<RechargeTransaction> builder)
    {
        builder.ToTable("RechargeTransactions");
        builder.HasKey(t => t.Id);

        // The database builds TXN-1001, TXN-1002 ... from the identity value.
        builder.Property(t => t.TransactionId)
            .HasMaxLength(20)
            .HasComputedColumnSql("'TXN-' + CAST([Id] + 1000 AS nvarchar(20))", stored: true);
        builder.HasIndex(t => t.TransactionId).IsUnique();

        builder.Property(t => t.MobileNumber).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Amount).HasColumnType("decimal(10,2)");

        builder.Property(t => t.Status)
            .HasMaxLength(10)
            .IsRequired()
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                text => Enum.Parse<RechargeStatus>(text, true));

        builder.Property(t => t.PaymentReference).HasMaxLength(30);
        builder.Property(t => t.FailureReason).HasMaxLength(200);

        builder.Property(t => t.CreatedAt).HasColumnType("datetime2");
        builder.Property(t => t.UpdatedAt).HasColumnType("datetime2");

        // Serves the history endpoint: one number, newest first.
        builder.HasIndex(t => new { t.MobileNumber, t.CreatedAt });
    }
}
