using Microsoft.Extensions.Logging.Abstractions;
using ZainRecharge.Application.Exceptions;
using ZainRecharge.Application.Providers;
using ZainRecharge.Application.Recharges;
using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Tests;

public class RechargeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static (RechargeService Service, FakeRechargeRepository Repository, FakeProviderClient Provider)
        Build(ProviderResult providerResult)
    {
        var repository = new FakeRechargeRepository();
        var provider = new FakeProviderClient(providerResult);
        var service = new RechargeService(
            repository, provider, new FixedTimeProvider(Now), NullLogger<RechargeService>.Instance);
        return (service, repository, provider);
    }

    private static ProviderResult Ok() => new(ProviderOutcome.Success, "PAY-50001", "Payment completed successfully");

    [Fact]
    public async Task Five_dinars_succeeds_and_stores_the_payment_reference()
    {
        var (service, repository, _) = Build(Ok());

        var result = await service.CreateAsync(new CreateRechargeRequest("0791234567", 5m), default);

        Assert.Equal(RechargeOutcome.Completed, result.Outcome);
        Assert.Equal("SUCCESS", result.Recharge.Status);
        Assert.Equal("PAY-50001", repository.Saved.Single().PaymentReference);
    }

    [Fact]
    public async Task The_provider_receives_the_database_generated_id_and_the_cleaned_number()
    {
        var (service, repository, provider) = Build(Ok());

        await service.CreateAsync(new CreateRechargeRequest("00962791234567", 5m), default);

        var call = Assert.Single(provider.Calls);
        Assert.Equal("TXN-1001", call.TransactionId);
        Assert.Equal("0791234567", call.MobileNumber);
        Assert.Equal("0791234567", repository.Saved.Single().MobileNumber);
    }

    [Fact]
    public async Task Twenty_five_dinars_is_declined_and_the_reason_is_stored()
    {
        var declined = new ProviderResult(ProviderOutcome.Declined, Message: "Amount exceeds the allowed limit of 20 JOD");
        var (service, repository, _) = Build(declined);

        var result = await service.CreateAsync(new CreateRechargeRequest("0791234567", 25m), default);

        Assert.Equal(RechargeOutcome.Declined, result.Outcome);
        Assert.Equal(RechargeStatus.Failed, repository.Saved.Single().Status);
        Assert.Equal("Amount exceeds the allowed limit of 20 JOD", repository.Saved.Single().FailureReason);
    }

    [Fact]
    public async Task A_negative_amount_never_reaches_the_database_or_the_provider()
    {
        var (service, repository, provider) = Build(Ok());

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(new CreateRechargeRequest("0791234567", -5m), default));

        Assert.Empty(repository.Saved);
        Assert.Empty(provider.Calls);
    }

    [Theory]
    [InlineData(ProviderOutcome.Success, RechargeStatus.Success, RechargeOutcome.Completed)]
    [InlineData(ProviderOutcome.Declined, RechargeStatus.Failed, RechargeOutcome.Declined)]
    [InlineData(ProviderOutcome.Unavailable, RechargeStatus.Failed, RechargeOutcome.ProviderUnavailable)]
    [InlineData(ProviderOutcome.Rejected, RechargeStatus.Failed, RechargeOutcome.ProviderError)]
    [InlineData(ProviderOutcome.ServerError, RechargeStatus.Unknown, RechargeOutcome.ProviderError)]
    [InlineData(ProviderOutcome.TimedOut, RechargeStatus.Unknown, RechargeOutcome.Unknown)]
    [InlineData(ProviderOutcome.NetworkError, RechargeStatus.Unknown, RechargeOutcome.Unknown)]
    public async Task Every_provider_outcome_maps_to_the_right_status_and_outcome(
        ProviderOutcome providerOutcome, RechargeStatus expectedStatus, RechargeOutcome expectedOutcome)
    {
        var providerResult = new ProviderResult(
            providerOutcome, providerOutcome == ProviderOutcome.Success ? "PAY-50001" : null, "message");
        var (service, repository, _) = Build(providerResult);

        var result = await service.CreateAsync(new CreateRechargeRequest("0791234567", 5m), default);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(expectedStatus, repository.Saved.Single().Status);
    }

    [Fact]
    public async Task History_returns_the_saved_transactions_newest_first()
    {
        var (service, _, _) = Build(Ok());
        await service.CreateAsync(new CreateRechargeRequest("0791234567", 1m), default);
        await service.CreateAsync(new CreateRechargeRequest("0791234567", 2m), default);
        await service.CreateAsync(new CreateRechargeRequest("0781234567", 3m), default);

        var history = await service.GetHistoryAsync("00962791234567", 1, 20, default);

        Assert.Equal(2, history.TotalCount);
        Assert.Equal(["TXN-1002", "TXN-1001"], history.Items.Select(i => i.TransactionId));
    }

    [Fact]
    public async Task An_unknown_id_is_not_found_and_an_id_below_one_is_invalid()
    {
        var (service, _, _) = Build(Ok());

        await Assert.ThrowsAsync<RechargeNotFoundException>(() => service.GetByIdAsync(99, default));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.GetByIdAsync(0, default));
    }

    [Fact]
    public void A_successful_recharge_can_never_turn_into_a_failed_one()
    {
        var transaction = RechargeTransaction.Create("0791234567", 5m, Now.UtcDateTime);
        transaction.MarkSuccess("PAY-50001", Now.UtcDateTime);

        Assert.Throws<InvalidOperationException>(() => transaction.MarkFailed("oops", Now.UtcDateTime));
    }
}
