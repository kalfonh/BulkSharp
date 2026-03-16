using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Processing.Storage.InMemory;

namespace BulkSharp.UnitTests.Storage;

[Trait("Category", "Unit")]
public class InMemoryBulkOperationRepositoryQueryTests
{
    private readonly InMemoryBulkOperationRepository _repo = new();

    private async Task<BulkOperation> SeedOperation(
        string operationName = "test-op",
        string createdBy = "admin",
        BulkOperationStatus status = BulkOperationStatus.Completed,
        DateTime? createdAt = null)
    {
        var op = new BulkOperation
        {
            OperationName = operationName,
            CreatedBy = createdBy,
            Status = status,
            FileName = "test.csv"
        };
        if (createdAt.HasValue)
            op.CreatedAt = createdAt.Value;
        return await _repo.CreateAsync(op);
    }

    [Fact]
    public async Task QueryAsync_NoFilters_ReturnsAllPaged()
    {
        for (int i = 0; i < 25; i++)
            await SeedOperation();

        var result = await _repo.QueryAsync(new BulkOperationQuery { Page = 1, PageSize = 10 });

        Assert.Equal(25, result.TotalCount);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task QueryAsync_FilterByOperationName_ReturnsMatching()
    {
        await SeedOperation(operationName: "user-import");
        await SeedOperation(operationName: "order-export");
        await SeedOperation(operationName: "user-import");

        var result = await _repo.QueryAsync(new BulkOperationQuery { OperationName = "user-import" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, op => Assert.Equal("user-import", op.OperationName));
    }

    [Fact]
    public async Task QueryAsync_FilterByCreatedBy_ReturnsMatching()
    {
        await SeedOperation(createdBy: "alice");
        await SeedOperation(createdBy: "bob");
        await SeedOperation(createdBy: "alice");

        var result = await _repo.QueryAsync(new BulkOperationQuery { CreatedBy = "alice" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, op => Assert.Equal("alice", op.CreatedBy));
    }

    [Fact]
    public async Task QueryAsync_FilterByStatus_ReturnsMatching()
    {
        await SeedOperation(status: BulkOperationStatus.Completed);
        await SeedOperation(status: BulkOperationStatus.Failed);
        await SeedOperation(status: BulkOperationStatus.Completed);

        var result = await _repo.QueryAsync(new BulkOperationQuery { Status = BulkOperationStatus.Failed });

        Assert.Single(result.Items);
        Assert.Equal(BulkOperationStatus.Failed, result.Items[0].Status);
    }

    [Fact]
    public async Task QueryAsync_FilterByDateRange_ReturnsMatching()
    {
        await SeedOperation(createdAt: new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        await SeedOperation(createdAt: new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc));
        await SeedOperation(createdAt: new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));

        var result = await _repo.QueryAsync(new BulkOperationQuery
        {
            FromDate = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc),
            ToDate = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_CombinedFilters_ReturnsMatchingAll()
    {
        await SeedOperation(operationName: "user-import", createdBy: "alice", status: BulkOperationStatus.Completed);
        await SeedOperation(operationName: "user-import", createdBy: "bob", status: BulkOperationStatus.Completed);
        await SeedOperation(operationName: "order-export", createdBy: "alice", status: BulkOperationStatus.Completed);

        var result = await _repo.QueryAsync(new BulkOperationQuery
        {
            OperationName = "user-import",
            CreatedBy = "alice"
        });

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_OperationNameContains_PartialMatch()
    {
        await SeedOperation(operationName: "user-import");
        await SeedOperation(operationName: "user-export");
        await SeedOperation(operationName: "order-import");

        var result = await _repo.QueryAsync(new BulkOperationQuery { OperationName = "user" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, op => Assert.Contains("user", op.OperationName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryAsync_CreatedByContains_PartialMatch()
    {
        await SeedOperation(createdBy: "alice@company.com");
        await SeedOperation(createdBy: "bob@company.com");
        await SeedOperation(createdBy: "alice@other.com");

        var result = await _repo.QueryAsync(new BulkOperationQuery { CreatedBy = "alice" });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_SortByOperationNameAscending()
    {
        await SeedOperation(operationName: "charlie-op");
        await SeedOperation(operationName: "alpha-op");
        await SeedOperation(operationName: "bravo-op");

        var result = await _repo.QueryAsync(new BulkOperationQuery
        {
            SortBy = "OperationName",
            SortDescending = false
        });

        Assert.Equal("alpha-op", result.Items[0].OperationName);
        Assert.Equal("bravo-op", result.Items[1].OperationName);
        Assert.Equal("charlie-op", result.Items[2].OperationName);
    }

    [Fact]
    public async Task QueryAsync_SortByTotalRowsDescending()
    {
        var op1 = await SeedOperation(operationName: "op1");
        op1.TotalRows = 10;
        var op2 = await SeedOperation(operationName: "op2");
        op2.TotalRows = 50;
        var op3 = await SeedOperation(operationName: "op3");
        op3.TotalRows = 30;

        var result = await _repo.QueryAsync(new BulkOperationQuery
        {
            SortBy = "TotalRows",
            SortDescending = true
        });

        Assert.Equal(50, result.Items[0].TotalRows);
        Assert.Equal(30, result.Items[1].TotalRows);
        Assert.Equal(10, result.Items[2].TotalRows);
    }

    [Fact]
    public async Task QueryAsync_ResultsOrderedByCreatedAtDescending()
    {
        await SeedOperation(createdAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedOperation(createdAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc));
        await SeedOperation(createdAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));

        var result = await _repo.QueryAsync(new BulkOperationQuery());

        Assert.Equal(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc), result.Items[0].CreatedAt);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), result.Items[1].CreatedAt);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), result.Items[2].CreatedAt);
    }
}
