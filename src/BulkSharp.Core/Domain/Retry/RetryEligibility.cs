namespace BulkSharp.Core.Domain.Retry;

public sealed class RetryEligibility
{
    public bool IsEligible { get; init; }
    public string? Reason { get; init; }

    public static RetryEligibility Eligible() => new() { IsEligible = true };
    public static RetryEligibility Ineligible(string reason) => new() { IsEligible = false, Reason = reason };
}
