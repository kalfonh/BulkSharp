namespace BulkSharp.Core.Domain.Operations;

public enum BulkErrorType
{
    None = 0,
    Validation = 1,
    Processing = 2,
    StepFailure = 3,
    Timeout = 4,
    SignalFailure = 5
}
