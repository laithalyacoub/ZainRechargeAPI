# Section 15 test files for ZainRechargeAPI. Run from C:\dev\zain\ZainRechargeAPI
$ErrorActionPreference = "Stop"
if (-not (Test-Path "tests\ZainRecharge.Tests")) { throw "Wrong folder. cd C:\dev\zain\ZainRechargeAPI first." }
$utf8 = New-Object System.Text.UTF8Encoding $false
function Write-Code($path, $content) {
  $full = Join-Path (Get-Location) $path
  New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
  [System.IO.File]::WriteAllText($full, $content.Replace("`r`n","`n").Replace("`n","`r`n") + "`r`n", $utf8)
  Write-Host "created $path"
}

# The template test file is not needed
Remove-Item "tests\ZainRecharge.Tests\UnitTest1.cs" -ErrorAction SilentlyContinue

Write-Code "tests/ZainRecharge.Tests/TestDoubles.cs" @'
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
'@

Write-Code "tests/ZainRecharge.Tests/RechargeRequestValidatorTests.cs" @'
using ZainRecharge.Application.Exceptions;
using ZainRecharge.Application.Recharges;

namespace ZainRecharge.Tests;

public class RechargeRequestValidatorTests
{
    [Theory]
    [InlineData("0791234567", "0791234567")]
    [InlineData("00962791234567", "0791234567")]   // the trainer's rule
    [InlineData("+962791234567", "0791234567")]
    [InlineData("079 123 4567", "0791234567")]
    [InlineData("079-123-4567", "0791234567")]
    [InlineData("00962 78 1234567", "0781234567")]
    [InlineData("0771234567", "0771234567")]
    public void Valid_numbers_are_cleaned(string input, string expected)
    {
        Assert.True(RechargeRequestValidator.TryNormalizeMobile(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("0701234567")]          // 70 is not 77, 78 or 79
    [InlineData("079123456")]           // too short
    [InlineData("07912345678")]         // too long
    [InlineData("07912ab567")]          // letters
    [InlineData("009620791234567")]     // 00962 followed by a leading 0
    [InlineData("\u0660\u0667\u0669\u0661\u0662\u0663\u0664\u0665\u0666\u0667")] // Arabic-Indic digits
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_numbers_are_rejected(string? input)
    {
        Assert.False(RechargeRequestValidator.TryNormalizeMobile(input, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(1000)]
    [InlineData(0.5)]
    public void Valid_amounts_are_accepted(double amount)
    {
        var valid = RechargeRequestValidator.ValidateAndNormalize(
            new CreateRechargeRequest("0791234567", (decimal)amount));

        Assert.Equal((decimal)amount, valid.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000.01)]
    [InlineData(5.123)]
    public void Invalid_amounts_are_rejected(double amount)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            RechargeRequestValidator.ValidateAndNormalize(
                new CreateRechargeRequest("0791234567", (decimal)amount)));

        Assert.True(ex.Errors.ContainsKey("amount"));
    }

    [Fact]
    public void A_missing_amount_and_a_bad_number_are_both_reported_at_once()
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            RechargeRequestValidator.ValidateAndNormalize(new CreateRechargeRequest("123", null)));

        Assert.True(ex.Errors.ContainsKey("mobileNumber"));
        Assert.True(ex.Errors.ContainsKey("amount"));
    }
}
'@

Write-Code "tests/ZainRecharge.Tests/RechargeServiceTests.cs" @'
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
'@

Write-Host "Done. Now run: dotnet test"
