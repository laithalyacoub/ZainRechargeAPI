using System.Text.RegularExpressions;
using ZainRecharge.Application.Exceptions;

namespace ZainRecharge.Application.Recharges;

public sealed record ValidatedRecharge(string MobileNumber, decimal Amount);

public static class RechargeRequestValidator
{
    public const decimal MaxAmount = 1000m;
    public const int MaxPageSize = 100;

    // [0-9] on purpose: \d would also accept Arabic-Indic digits.
    private static readonly Regex ValidMobile = new(@"^07[789][0-9]{7}$", RegexOptions.Compiled);

    /// <summary>Step 1 to 4 of rule 1: clean the number, then check it.</summary>
    public static bool TryNormalizeMobile(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // 1. remove spaces, dashes and brackets
        var cleaned = new string(input
            .Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '(' && c != ')')
            .ToArray());

        // 2. 00962 or +962 becomes 0
        if (cleaned.StartsWith("00962", StringComparison.Ordinal))
            cleaned = "0" + cleaned[5..];
        else if (cleaned.StartsWith("+962", StringComparison.Ordinal))
            cleaned = "0" + cleaned[4..];

        // 3. must now be 077, 078 or 079 followed by 7 digits
        if (!ValidMobile.IsMatch(cleaned)) return false;

        normalized = cleaned;
        return true;
    }

    public static ValidatedRecharge ValidateAndNormalize(CreateRechargeRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (!TryNormalizeMobile(request.MobileNumber, out var mobile))
            errors["mobileNumber"] = ["Mobile number must start with 077, 078 or 079 and have 10 digits. The prefixes 00962 and +962 are accepted."];

        var amount = 0m;
        if (request.Amount is null)
            errors["amount"] = ["Amount is required."];
        else if (request.Amount <= 0)
            errors["amount"] = ["Amount must be greater than 0."];
        else if (request.Amount > MaxAmount)
            errors["amount"] = [$"Amount cannot be more than {MaxAmount:0} JOD."];
        else if (decimal.Round(request.Amount.Value, 2) != request.Amount.Value)
            errors["amount"] = ["Amount can have at most 2 decimal places."];
        else
            amount = request.Amount.Value;

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return new ValidatedRecharge(mobile, amount);
    }

    public static void ValidateId(int id)
    {
        if (id < 1)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["id"] = ["Id must be 1 or more."] });
    }

    public static (string MobileNumber, int Page, int PageSize) ValidateHistoryQuery(
        string? mobileNumber, int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();

        if (!TryNormalizeMobile(mobileNumber, out var mobile))
            errors["mobileNumber"] = ["Mobile number must start with 077, 078 or 079 and have 10 digits."];
        if (page < 1)
            errors["page"] = ["Page must be 1 or more."];
        if (pageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return (mobile, page, pageSize);
    }
}
