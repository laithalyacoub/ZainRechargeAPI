namespace ZainRecharge.Application.Exceptions;

// becomes 400
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : Exception("The request is not valid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

// becomes 404
public sealed class RechargeNotFoundException(int id)
    : Exception($"Recharge {id} was not found.");
