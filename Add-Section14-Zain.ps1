# Section 14 files for ZainRechargeAPI. Run from C:\dev\zain\ZainRechargeAPI
$ErrorActionPreference = "Stop"
if (-not (Test-Path "src\ZainRecharge.Api")) { throw "Wrong folder. cd C:\dev\zain\ZainRechargeAPI first." }
$utf8 = New-Object System.Text.UTF8Encoding $false
function Write-Code($path, $content) {
  $full = Join-Path (Get-Location) $path
  New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
  [System.IO.File]::WriteAllText($full, $content.Replace("`r`n","`n").Replace("`n","`r`n") + "`r`n", $utf8)
  Write-Host "created $path"
}

# 1. Packages (Serilog.AspNetCore brings the console sink and the appsettings reader)
dotnet add src\ZainRecharge.Api package Serilog.AspNetCore
dotnet add src\ZainRecharge.Api package Serilog.Sinks.Seq
if ($LASTEXITCODE -ne 0) { throw "Package install failed. Check your internet connection and run the script again." }

# 2. Files

Write-Code "src/ZainRecharge.Api/Middleware/CorrelationIdMiddleware.cs" @'
using Serilog.Context;

namespace ZainRecharge.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context);

        context.Items[HeaderName] = correlationId; // for code that needs it later in this request
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId; // the caller gets it back
            return Task.CompletedTask;
        });

        // Every log line written while this request runs now carries CorrelationId.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ReadOrCreate(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        return IsSafe(incoming) ? incoming : Guid.NewGuid().ToString("N");
    }

    // A value from outside goes into our logs, so only short and harmless values are accepted.
    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
'@

Write-Code "src/ZainRecharge.Application/Common/ICorrelationIdAccessor.cs" @'
namespace ZainRecharge.Application.Common;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
'@

Write-Code "src/ZainRecharge.Api/Middleware/HttpContextCorrelationIdAccessor.cs" @'
using ZainRecharge.Application.Common;

namespace ZainRecharge.Api.Middleware;

public sealed class HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    : ICorrelationIdAccessor
{
    public string? CorrelationId =>
        httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.HeaderName] as string;
}
'@

Write-Code "src/ZainRecharge.Infrastructure/Providers/CorrelationIdHandler.cs" @'
using ZainRecharge.Application.Common;

namespace ZainRecharge.Infrastructure.Providers;

internal sealed class CorrelationIdHandler(ICorrelationIdAccessor correlation) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (correlation.CorrelationId is { Length: > 0 } id)
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", id);

        return base.SendAsync(request, cancellationToken);
    }
}
'@

Write-Code "src/ZainRecharge.Infrastructure/DependencyInjection.cs" @'
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
        services.AddTransient<CorrelationIdHandler>();

        services.AddHttpClient(HttpClientNames.Token, (sp, client) =>
                ConfigureProviderClient(sp, client, o => o.TokenTimeoutSeconds))
            .AddHttpMessageHandler<CorrelationIdHandler>();

        services.AddHttpClient(HttpClientNames.Payment, (sp, client) =>
                ConfigureProviderClient(sp, client, o => o.PaymentTimeoutSeconds))
            .AddHttpMessageHandler<CorrelationIdHandler>();

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
'@

Write-Code "src/ZainRecharge.Api/Program.cs" @'
using System.Diagnostics;
using Microsoft.OpenApi;
using ZainRecharge.Api.ErrorHandling;
using ZainRecharge.Application.Recharges;
using ZainRecharge.Infrastructure;
using ZainRecharge.Infrastructure.Persistence;
using ZainRecharge.Infrastructure.Providers;
using Serilog;
using ZainRecharge.Api.Middleware;
using ZainRecharge.Application.Common;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration));

    // ---------- Settings (checked at startup) ----------
    builder.Services
        .AddOptions<PaymentProviderOptions>()
        .Bind(builder.Configuration.GetSection(PaymentProviderOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ---------- Our services ----------
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddScoped<IRechargeService, RechargeService>();

    // ---------- Errors: one JSON shape for every failure ----------
    builder.Services.AddProblemDetails(options =>
        options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
    builder.Services.AddExceptionHandler<ApiExceptionHandler>();

    // ---------- Web API ----------
    builder.Services.AddControllers();
    builder.Services.AddHealthChecks().AddDbContextCheck<ZainDbContext>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Zain Recharge API", Version = "v1" }));

    // Zain only: how Infrastructure learns the current correlation id.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();   // first, so everything below carries the id
    app.UseSerilogRequestLogging();                  // one summary line per request

    // ---------- The pipeline: the order matters ----------
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.MapControllers();
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "The application stopped during startup");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
'@

# appsettings.json: add the Serilog section
$cfg = Join-Path (Get-Location) "src\ZainRecharge.Api\appsettings.json"
$j = [System.IO.File]::ReadAllText($cfg)
if (-not $j.Contains('"Serilog"')) {
  $block = @'
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.Seq" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "System.Net.Http.HttpClient": "Warning"
      }
    },
    "Enrich": [ "FromLogContext" ],
    "Properties": { "Application": "ZainRechargeAPI" },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      { "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }
    ]
  },
'@
  $idx = $j.IndexOf("{")
  $j = $block + $j.Substring($idx + 1)
  [System.IO.File]::WriteAllText($cfg, $j, $utf8)
  Write-Host "patched src/ZainRecharge.Api/appsettings.json"
} else { Write-Host "appsettings.json already has Serilog, left alone" }


Write-Host "Done. Now run: dotnet build"