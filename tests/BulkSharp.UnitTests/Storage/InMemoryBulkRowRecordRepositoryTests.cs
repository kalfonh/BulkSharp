using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Processing.Storage.InMemory;

namespace BulkSharp.UnitTests.Storage;

[Trait("Category", "Unit")]
public class InMemoryBulkRowRecordRepositoryTests
{
    private readonly InMemoryBulkRowRecordRepository _repo = new();
    private readonly Guid _operationId = Guid.NewGuid();

    [Fact]
    public async Task CreateBatch_and_QueryAll_ReturnsInsertedRecords()
    {
        var records = Enumerable.Range(1, 5)
            .Select(i => BulkRowRecord.CreateValidation(_operationId, i))
            .ToList();

        await _repo.CreateBatchAsync(records);

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId });
        result.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task UpdateBatch_ReflectsStateChange()
    {
        var record = BulkRowRecord.CreateValidation(_operationId, 1);
        await _repo.CreateAsync(record);

        record.MarkCompleted();
        await _repo.UpdateBatchAsync(new[] { record });

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, State = RowRecordState.Completed });
        result.TotalCount.Should().Be(1);
        result.Items[0].State.Should().Be(RowRecordState.Completed);
        result.Items[0].CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBySignalKey_ReturnsCorrectRecord()
    {
        var record = BulkRowRecord.CreateStep(_operationId, 1, "row-1", "step-a", 0);
        record.SignalKey = "signal-abc";
        record.MarkWaitingForCompletion();
        await _repo.CreateAsync(record);

        var found = await _repo.GetBySignalKeyAsync("signal-abc");
        found.Should().NotBeNull();
        found!.RowNumber.Should().Be(1);
        found.SignalKey.Should().Be("signal-abc");
    }

    [Fact]
    public async Task GetBySignalKey_WrongState_ReturnsNull()
    {
        var record = BulkRowRecord.CreateStep(_operationId, 1, "row-1", "step-a", 0);
        record.SignalKey = "signal-xyz";
        // State is Running (from CreateStep), not WaitingForCompletion
        await _repo.CreateAsync(record);

        var found = await _repo.GetBySignalKeyAsync("signal-xyz");
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetByOperationRowStep_ReturnsCorrectRecord()
    {
        var r1 = BulkRowRecord.CreateStep(_operationId, 1, null, "step-a", 0);
        var r2 = BulkRowRecord.CreateStep(_operationId, 1, null, "step-b", 1);
        var r3 = BulkRowRecord.CreateStep(_operationId, 2, null, "step-a", 0);
        await _repo.CreateBatchAsync(new[] { r1, r2, r3 });

        var found = await _repo.GetByOperationRowStepAsync(_operationId, 1, 1);
        found.Should().NotBeNull();
        found!.StepName.Should().Be("step-b");
    }

    [Fact]
    public async Task GetByOperationRowStep_NotFound_ReturnsNull()
    {
        var found = await _repo.GetByOperationRowStepAsync(_operationId, 99, 99);
        found.Should().BeNull();
    }

    [Fact]
    public async Task Query_FilterByState()
    {
        var pending = BulkRowRecord.CreateValidation(_operationId, 1);
        var completed = BulkRowRecord.CreateValidation(_operationId, 2);
        completed.MarkCompleted();
        await _repo.CreateBatchAsync(new[] { pending, completed });

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, State = RowRecordState.Pending });
        result.TotalCount.Should().Be(1);
        result.Items[0].RowNumber.Should().Be(1);
    }

    [Fact]
    public async Task Query_FilterByErrorType()
    {
        var ok = BulkRowRecord.CreateValidation(_operationId, 1);
        ok.MarkCompleted();
        var failed = BulkRowRecord.CreateValidation(_operationId, 2);
        failed.MarkFailed("bad data", BulkErrorType.Validation);
        await _repo.CreateBatchAsync(new[] { ok, failed });

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, ErrorType = BulkErrorType.Validation });
        result.TotalCount.Should().Be(1);
        result.Items[0].RowNumber.Should().Be(2);
        result.Items[0].ErrorType.Should().Be(BulkErrorType.Validation);
    }

    [Fact]
    public async Task Query_ErrorsOnly_ReturnsFailedAndTimedOut()
    {
        var ok = BulkRowRecord.CreateValidation(_operationId, 1);
        ok.MarkCompleted();
        var failed = BulkRowRecord.CreateValidation(_operationId, 2);
        failed.MarkFailed("err", BulkErrorType.Processing);
        var timedOut = BulkRowRecord.CreateValidation(_operationId, 3);
        timedOut.MarkTimedOut("step-x");
        await _repo.CreateBatchAsync(new[] { ok, failed, timedOut });

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, ErrorsOnly = true });
        result.TotalCount.Should().Be(2);
        result.Items.Select(r => r.RowNumber).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public async Task Query_FilterByRowNumberRange()
    {
        var records = Enumerable.Range(1, 10)
            .Select(i => BulkRowRecord.CreateValidation(_operationId, i))
            .ToList();
        await _repo.CreateBatchAsync(records);

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, FromRowNumber = 3, ToRowNumber = 7 });
        result.TotalCount.Should().Be(5);
        result.Items.Select(r => r.RowNumber).Should().BeEquivalentTo(new[] { 3, 4, 5, 6, 7 });
    }

    [Fact]
    public async Task Query_FilterByStepIndex()
    {
        var r1 = BulkRowRecord.CreateStep(_operationId, 1, null, "step-a", 0);
        var r2 = BulkRowRecord.CreateStep(_operationId, 1, null, "step-b", 1);
        await _repo.CreateBatchAsync(new[] { r1, r2 });

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, StepIndex = 1 });
        result.TotalCount.Should().Be(1);
        result.Items[0].StepName.Should().Be("step-b");
    }

    [Fact]
    public async Task Query_FilterByRowId_ContainsMatch()
    {
        var r1 = BulkRowRecord.CreateValidation(_operationId, 1, rowId: "user-abc-123");
        var r2 = BulkRowRecord.CreateValidation(_operationId, 2, rowId: "user-xyz-456");
        await _repo.CreateBatchAsync(new[] { r1, r2 });

        var result = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, RowId = "abc" });
        result.TotalCount.Should().Be(1);
        result.Items[0].RowNumber.Should().Be(1);
    }

    [Fact]
    public async Task Query_Pagination_ReturnsCorrectPage()
    {
        var records = Enumerable.Range(1, 25)
            .Select(i => BulkRowRecord.CreateValidation(_operationId, i))
            .ToList();
        await _repo.CreateBatchAsync(records);

        var page2 = await _repo.QueryAsync(new BulkRowRecordQuery { OperationId = _operationId, Page = 2, PageSize = 10 });
        page2.TotalCount.Should().Be(25);
        page2.Items.Should().HaveCount(10);
        page2.Items[0].RowNumber.Should().Be(11);
    }

    [Fact]
    public async Task QueryDistinctRowNumbers_ReturnsPaged()
    {
        // Row 1 has two steps, row 2 has one step
        var r1 = BulkRowRecord.CreateStep(_operationId, 1, null, "step-a", 0);
        var r2 = BulkRowRecord.CreateStep(_operationId, 1, null, "step-b", 1);
        var r3 = BulkRowRecord.CreateStep(_operationId, 2, null, "step-a", 0);
        await _repo.CreateBatchAsync(new[] { r1, r2, r3 });

        var result = await _repo.QueryDistinctRowNumbersAsync(_operationId, 1, 100);
        result.TotalCount.Should().Be(2);
        result.Items.Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
