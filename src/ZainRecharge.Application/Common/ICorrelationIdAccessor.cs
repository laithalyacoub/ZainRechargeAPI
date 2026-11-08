namespace ZainRecharge.Application.Common;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
