using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.UnitTests.Retry;

public class BulkRowRecordResetTests
{
    [Fact]
    public void ResetForRetry_ShouldSetStateToPending()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Equal(RowRecordState.Pending, record.State);
    }

    [Fact]
    public void ResetForRetry_ShouldIncrementRetryAttempt()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Equal(1, record.RetryAttempt);
    }

    [Fact]
    public void ResetForRetry_ShouldSetRetryFromStepIndex()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Equal(2, record.RetryFromStepIndex);
    }

    [Fact]
    public void ResetForRetry_ShouldClearErrorFields()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Null(record.ErrorType);
        Assert.Null(record.ErrorMessage);
        Assert.Null(record.CompletedAt);
    }

    [Fact]
    public void ResetForRetry_CalledTwice_ShouldIncrementRetryAttemptEachTime()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("first error", BulkErrorType.Processing);
        record.ResetForRetry(2);

        record.MarkFailed("second error", BulkErrorType.Processing);
        record.ResetForRetry(2);

        Assert.Equal(2, record.RetryAttempt);
    }
}
