using Microsoft.Extensions.Logging;
using ZainRecharge.Application.Common;
using ZainRecharge.Application.Exceptions;
using ZainRecharge.Application.Providers;
using ZainRecharge.Domain.Entities;

namespace ZainRecharge.Application.Recharges;

public sealed class RechargeService(
    IRechargeRepository repository,
    IPaymentProviderClient providerClient,
    TimeProvider clock,
    ILogger<RechargeService> logger) : IRechargeService
{
    public async Task<RechargeResult> CreateAsync(CreateRechargeRequest request, CancellationToken cancellationToken)
    {
        // 1. Clean and validate. Nothing is saved if this fails. (400)
        var valid = RechargeRequestValidator.ValidateAndNormalize(request);

        // 2. Save first, as PENDING. The database now generates TXN-xxxx.
        var transaction = RechargeTransaction.Create(valid.MobileNumber, valid.Amount, Now());
        await repository.AddAsync(transaction, cancellationToken);

        // Every log line below carries the TransactionId automatically.
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TransactionId"] = transaction.TransactionId
        });

        logger.LogInformation(
            "Recharge created as PENDING for {MobileNumber}, amount {Amount}",
            MobileMasking.Mask(transaction.MobileNumber), transaction.Amount);

        // 3. Call the provider. The caller's cancellation is NOT passed on: once the payment is
        //    on its way we must see it through and record the result. The HTTP client has its own timeout.
        var provider = await providerClient.ProcessPaymentAsync(
            new ProviderPaymentRequest(transaction.TransactionId, transaction.MobileNumber, transaction.Amount),
            CancellationToken.None);

        // 4. Move the row to its final status.
        var outcome = Apply(transaction, provider);

        try
        {
            await repository.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The provider has answered but we could not write it down. The row is still PENDING.
            // This must be investigated, so it is logged at the highest level.
            logger.LogCritical(ex,
                "Provider outcome {ProviderOutcome} could not be saved; the transaction needs reconciliation",
                provider.Outcome);
            throw;
        }

        logger.LogInformation(
            "Recharge finished as {Status} (provider outcome {ProviderOutcome})",
            transaction.Status, provider.Outcome);

        return new RechargeResult(outcome, RechargeMapper.ToResponse(transaction));
    }

    public async Task<RechargeResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        RechargeRequestValidator.ValidateId(id);

        var transaction = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new RechargeNotFoundException(id);

        return RechargeMapper.ToResponse(transaction);
    }

    public async Task<RechargeHistoryResponse> GetHistoryAsync(
        string? mobileNumber, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = RechargeRequestValidator.ValidateHistoryQuery(mobileNumber, page, pageSize);

        var (items, totalCount) = await repository.GetHistoryAsync(
            query.MobileNumber, query.Page, query.PageSize, cancellationToken);

        return new RechargeHistoryResponse(
            query.MobileNumber, query.Page, query.PageSize, totalCount,
            items.Select(RechargeMapper.ToResponse).ToList());
    }

    // The one place that turns "what the provider said" into "what we record and what we tell the caller".
    private RechargeOutcome Apply(RechargeTransaction transaction, ProviderResult provider)
    {
        var now = Now();

        switch (provider.Outcome)
        {
            case ProviderOutcome.Success:
                transaction.MarkSuccess(provider.PaymentReference ?? "", now);
                return RechargeOutcome.Completed;

            case ProviderOutcome.Declined:
                transaction.MarkFailed(provider.Message ?? "The payment was declined", now);
                return RechargeOutcome.Declined;

            case ProviderOutcome.Unavailable:
                transaction.MarkFailed("The payment provider is unavailable. The payment was not attempted.", now);
                return RechargeOutcome.ProviderUnavailable;

            case ProviderOutcome.Rejected:
                transaction.MarkFailed("The payment provider rejected the request. The payment was not attempted.", now);
                return RechargeOutcome.ProviderError;

            case ProviderOutcome.ServerError:
                transaction.MarkUnknown("The payment provider failed while processing. The result is not known.", now);
                return RechargeOutcome.ProviderError;

            case ProviderOutcome.TimedOut:
                transaction.MarkUnknown("The payment provider did not answer in time. The result is not known.", now);
                return RechargeOutcome.Unknown;

            default: // NetworkError
                transaction.MarkUnknown("The connection to the payment provider broke. The result is not known.", now);
                return RechargeOutcome.Unknown;
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
