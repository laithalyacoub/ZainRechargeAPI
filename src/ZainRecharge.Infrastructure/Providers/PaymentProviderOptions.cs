using System.ComponentModel.DataAnnotations;

namespace ZainRecharge.Infrastructure.Providers;

public sealed class PaymentProviderOptions
{
    public const string SectionName = "PaymentProvider";

    [Required, Url] public string BaseUrl { get; set; } = "";
    [Required] public string ApiKey { get; set; } = "";
    [Required] public string ClientId { get; set; } = "";
    [Required] public string ClientSecret { get; set; } = "";

    [Range(1, 60)] public int PaymentTimeoutSeconds { get; set; } = 5;
    [Range(1, 60)] public int TokenTimeoutSeconds { get; set; } = 5;

    // Ask for a new token this many seconds BEFORE the old one expires.
    [Range(0, 120)] public int TokenRefreshMarginSeconds { get; set; } = 30;
}
