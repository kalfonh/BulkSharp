using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.UnitTests.Retry;

public class BulkRowRetryHistoryTests
{
    [Fact]
    public void CreateFromRecord_ShouldSnapshotFailedState()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 5, "row5", "ValidateAddress", 2);
        record.MarkFailed("Invalid zip code", BulkErrorType.Validation);
        record.RowData = """{"name":"John","zip":"00000"}""";

        var history = BulkRowRetryHistory.CreateFromRecord(record);

        Assert.Equal(record.BulkOperationId, history.BulkOperationId);
        Assert.Equal(5, history.RowNumber);
        Assert.Equal(2, history.StepIndex);
        Assert.Equal(0, history.Attempt);
        Assert.Equal(BulkErrorType.Validation, history.ErrorType);
        Assert.Equal("Invalid zip code", history.ErrorMessage);
        Assert.Equal(record.RowData, history.RowData);
        Assert.NotEqual(default, history.FailedAt);
    }

    [Fact]
    public void CreateFromRecord_WithRetryAttempt_ShouldUseRecordRetryAttempt()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, null, "step1", 0);
        record.MarkFailed("error", BulkErrorType.Processing);
        record.ResetForRetry(0);
        record.MarkFailed("error again", BulkErrorType.Processing);

        var history = BulkRowRetryHistory.CreateFromRecord(record);

        Assert.Equal(1, history.Attempt);
    }
}
