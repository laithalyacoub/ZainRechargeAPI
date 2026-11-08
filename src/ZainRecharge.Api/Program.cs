using System.Diagnostics;
using Microsoft.OpenApi;
using ZainRecharge.Api.ErrorHandling;
using ZainRecharge.Application.Recharges;
using ZainRecharge.Infrastructure;
using ZainRecharge.Infrastructure.Persistence;
using ZainRecharge.Infrastructure.Providers;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

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

public partial class Program;
