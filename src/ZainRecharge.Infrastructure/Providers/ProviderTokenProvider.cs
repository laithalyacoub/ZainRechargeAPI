using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZainRecharge.Infrastructure.Providers;

internal sealed class ProviderTokenUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

internal interface IProviderTokenProvider
{
    Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

internal sealed class ProviderTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentProviderOptions> options,
    TimeProvider clock,
    ILogger<ProviderTokenProvider> logger) : IProviderTokenProvider
{
    private sealed record CachedToken(string Value, DateTimeOffset RefreshAt);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile CachedToken? _cached;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && TryGetFresh(out var fresh))
            return fresh;

        // Only one request at a time asks the provider for a token.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryGetFresh(out fresh))
                return fresh;

            return await RequestNewTokenAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFresh(out string token)
    {
        var cached = _cached;
        if (cached is not null && clock.GetUtcNow() < cached.RefreshAt)
        {
            token = cached.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<string> RequestNewTokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient(HttpClientNames.Token);

        try
        {
            using var response = await client.PostAsJsonAsync(
                "api/auth/token",
                new { clientId = settings.ClientId, clientSecret = settings.ClientSecret },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 401 here means the API key or the credentials are wrong: a configuration problem.
                logger.LogError("The provider answered {StatusCode} to the token request", (int)response.StatusCode);
                throw new ProviderTokenUnavailableException(
                    $"The provider answered {(int)response.StatusCode} to the token request.");
            }

            var body = await response.Content.ReadFromJsonAsync<ProviderTokenBody>(cancellationToken);
            if (body is null || string.IsNullOrWhiteSpace(body.AccessToken) || body.ExpiresInSeconds <= 0)
                throw new ProviderTokenUnavailableException("The token response from the provider was not valid.");

            var refreshInSeconds = Math.Max(1, body.ExpiresInSeconds - settings.TokenRefreshMarginSeconds);
            _cached = new CachedToken(body.AccessToken, clock.GetUtcNow().AddSeconds(refreshInSeconds));

            logger.LogInformation(
                "Obtained a provider token valid for {ExpiresInSeconds} seconds", body.ExpiresInSeconds);
            return body.AccessToken;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "The token request to the provider timed out");
            throw new ProviderTokenUnavailableException("The token request to the provider timed out.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            logger.LogError(ex, "The token request to the provider failed");
            throw new ProviderTokenUnavailableException("The token request to the provider failed.", ex);
        }
    }
}
