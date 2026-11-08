using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZainRecharge.Application.Providers;

namespace ZainRecharge.Infrastructure.Providers;

internal sealed class PaymentProviderClient(
    IHttpClientFactory httpClientFactory,
    IProviderTokenProvider tokens,
    ILogger<PaymentProviderClient> logger) : IPaymentProviderClient
{
    public async Task<ProviderResult> ProcessPaymentAsync(
        ProviderPaymentRequest request, CancellationToken cancellationToken)
    {
        // Attempt 1 uses the cached token. If the provider says 401, attempt 2 uses a fresh one.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            string token;
            try
            {
                token = await tokens.GetTokenAsync(forceRefresh: attempt == 2, cancellationToken);
            }
            catch (ProviderTokenUnavailableException ex)
            {
                logger.LogError(ex, "No provider token available; the payment was not sent");
                return new ProviderResult(ProviderOutcome.Unavailable, Message: ex.Message);
            }

            var (result, unauthorized) = await SendOnceAsync(request, token, cancellationToken);

            if (unauthorized && attempt == 1)
            {
                logger.LogWarning("The provider rejected the token; requesting a new one and trying once more");
                continue;
            }

            return result;
        }

        return new ProviderResult(ProviderOutcome.Rejected, Message: "The provider rejected the token twice.");
    }

    private async Task<(ProviderResult Result, bool Unauthorized)> SendOnceAsync(
        ProviderPaymentRequest request, string token, CancellationToken cancellationToken)
    {
        // A request message can be sent only once, so every attempt builds a new one.
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/payments/process")
        {
            Content = JsonContent.Create(new
            {
                transactionId = request.TransactionId,
                mobileNumber = request.MobileNumber,
                amount = request.Amount
            })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client = httpClientFactory.CreateClient(HttpClientNames.Payment);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            return await InterpretAsync(response, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient timeout fired. We do not know whether the provider finished the payment.
            logger.LogWarning(ex, "The payment call timed out");
            return (new ProviderResult(ProviderOutcome.TimedOut), false);
        }
        catch (HttpRequestException ex) when (
            ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
        {
            // We never reached the provider, so the payment certainly did not happen.
            logger.LogError(ex, "Could not connect to the provider; the payment was not sent");
            return (new ProviderResult(ProviderOutcome.Unavailable, Message: "The provider could not be reached."), false);
        }
        catch (HttpRequestException ex)
        {
            // The request may have been received before the connection broke.
            logger.LogWarning(ex, "The connection broke during the payment call");
            return (new ProviderResult(ProviderOutcome.NetworkError), false);
        }
    }

    private async Task<(ProviderResult Result, bool Unauthorized)> InterpretAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = (int)response.StatusCode;
        logger.LogInformation("The provider answered {StatusCode}", code);

        if (response.StatusCode == HttpStatusCode.OK)
            return (await InterpretOkAsync(response, cancellationToken), false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return (new ProviderResult(ProviderOutcome.Rejected, Message: "Unauthorized"), true);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return (new ProviderResult(ProviderOutcome.Unavailable, Message: "The provider answered 503."), false);

        if (code >= 500)
            return (new ProviderResult(ProviderOutcome.ServerError, Message: $"The provider answered {code}."), false);

        return (new ProviderResult(ProviderOutcome.Rejected, Message: $"The provider answered {code}."), false);
    }

    private async Task<ProviderResult> InterpretOkAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ProviderPaymentBody? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<ProviderPaymentBody>(cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogError(ex, "The provider answered 200 with a body that could not be read");
            return new ProviderResult(ProviderOutcome.ServerError, Message: "Unreadable provider answer.");
        }

        if (body is null)
            return new ProviderResult(ProviderOutcome.ServerError, Message: "Empty provider answer.");

        if (string.Equals(body.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(body.PaymentReference))
            return new ProviderResult(ProviderOutcome.Success, body.PaymentReference, body.Message);

        if (string.Equals(body.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            return new ProviderResult(ProviderOutcome.Declined, Message: body.Message);

        logger.LogError("The provider answered 200 with an unexpected status {ProviderStatus}", body?.Status);
        return new ProviderResult(ProviderOutcome.ServerError, Message: "Unexpected provider answer.");
    }
}
