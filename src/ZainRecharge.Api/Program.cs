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
