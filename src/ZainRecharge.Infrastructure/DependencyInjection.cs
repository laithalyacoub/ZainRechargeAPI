using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZainRecharge.Application.Providers;
using ZainRecharge.Application.Recharges;
using ZainRecharge.Infrastructure.Persistence;
using ZainRecharge.Infrastructure.Providers;

namespace ZainRecharge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ZainDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.EnableRetryOnFailure()));

        services.AddScoped<IRechargeRepository, RechargeRepository>();

        // The provider: two named clients, one token cache, one client class.
        services.AddHttpClient(HttpClientNames.Token, (sp, client) =>
            ConfigureProviderClient(sp, client, o => o.TokenTimeoutSeconds));

        services.AddHttpClient(HttpClientNames.Payment, (sp, client) =>
            ConfigureProviderClient(sp, client, o => o.PaymentTimeoutSeconds));

        services.AddSingleton<IProviderTokenProvider, ProviderTokenProvider>(); // holds the cached token
        services.AddScoped<IPaymentProviderClient, PaymentProviderClient>();

        return services;
    }

    private static void ConfigureProviderClient(
        IServiceProvider sp, HttpClient client, Func<PaymentProviderOptions, int> timeoutSeconds)
    {
        var settings = sp.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;

        // The trailing slash matters: it lets "api/payments/process" be added after the base address.
        client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds(settings));
        client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
    }
}
