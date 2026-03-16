namespace BulkSharp.Core.Domain.Operations;

public enum RowRecordState
{
    Pending,
    Running,
    WaitingForCompletion,
    Completed,
    Failed,
    TimedOut
}
