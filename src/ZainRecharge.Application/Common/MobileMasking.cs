namespace ZainRecharge.Application.Common;

public static class MobileMasking
{
    // 0791234567 becomes 0791***567. Use this in every log message.
    public static string Mask(string? mobileNumber) =>
        mobileNumber is { Length: >= 7 }
            ? $"{mobileNumber[..4]}***{mobileNumber[^3..]}"
            : "***";
}
