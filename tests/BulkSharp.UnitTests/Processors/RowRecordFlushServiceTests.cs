using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Processors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BulkSharp.UnitTests.Processors;

[Trait("Category", "Unit")]
public class RowRecordFlushServiceTests
{
    private readonly Mock<IBulkRowRecordRepository> _repoMock = new();

    private RowRecordFlushService CreateService() => new(_repoMock.Object, NullLogger<RowRecordFlushService>.Instance);

    [Fact]
    public async Task FlushAsync_WithPendingCreates_CallsCreateBatchAsync()
    {
        var service = CreateService();
        var record = new BulkRowRecord { BulkOperationId = Guid.NewGuid() };

        List<BulkRowRecord>? captured = null;
        _repoMock.Setup(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BulkRowRecord>, CancellationToken>((items, _) => captured = items.ToList())
            .Returns(Task.CompletedTask);

        service.TrackCreate(record);
        await service.FlushAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Single(captured);
        Assert.Same(record, captured[0]);
    }

    [Fact]
    public async Task FlushAsync_WithPendingUpdates_CallsUpdateBatchAsync()
    {
        var service = CreateService();
        var record = new BulkRowRecord { BulkOperationId = Guid.NewGuid() };

        List<BulkRowRecord>? captured = null;
        _repoMock.Setup(r => r.UpdateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BulkRowRecord>, CancellationToken>((items, _) => captured = items.ToList())
            .Returns(Task.CompletedTask);

        service.TrackUpdate(record);
        await service.FlushAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Single(captured);
        Assert.Same(record, captured[0]);
    }

    [Fact]
    public async Task FlushAsync_WithNothingPending_DoesNotCallRepository()
    {
        var service = CreateService();
        await service.FlushAsync(CancellationToken.None);

        _repoMock.Verify(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.UpdateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlushAsync_CreateBatchThrows_DoesNotPropagate()
    {
        var service = CreateService();
        var record = new BulkRowRecord { BulkOperationId = Guid.NewGuid() };

        _repoMock.Setup(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        service.TrackCreate(record);
        await service.FlushAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FlushAsync_UpdateBatchThrows_DoesNotPropagate()
    {
        var service = CreateService();
        var record = new BulkRowRecord { BulkOperationId = Guid.NewGuid() };

        _repoMock.Setup(r => r.UpdateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        service.TrackUpdate(record);
        await service.FlushAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FlushAsync_ClearsBufferAfterFlush()
    {
        var service = CreateService();
        service.TrackCreate(new BulkRowRecord { BulkOperationId = Guid.NewGuid() });
        service.TrackUpdate(new BulkRowRecord { BulkOperationId = Guid.NewGuid() });

        await service.FlushAsync(CancellationToken.None);

        _repoMock.Invocations.Clear();
        await service.FlushAsync(CancellationToken.None);

        _repoMock.Verify(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.UpdateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
