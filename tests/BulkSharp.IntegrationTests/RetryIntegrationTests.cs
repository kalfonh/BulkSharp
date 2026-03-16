using System.Collections.Concurrent;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Domain.Retry;
using BulkSharp.Processing.Storage.InMemory;
using BulkSharp.Core.Configuration;

namespace BulkSharp.IntegrationTests;

/// <summary>
/// Shared static tracker for retry test operations.
/// Keyed by test run ID to isolate parallel test runs.
/// </summary>
internal static class RetryTestTracker
{
    public static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> Trackers = new();
    public static readonly ConcurrentDictionary<string, ConcurrentBag<string>> AlwaysFailNames = new();

    [ThreadStatic]
    public static string? CurrentTestRunId;

    public static ConcurrentDictionary<string, int> GetTracker()
    {
        return Trackers.GetOrAdd(CurrentTestRunId ?? "default", _ => new ConcurrentDictionary<string, int>());
    }

    public static void Reset(string testRunId)
    {
        Trackers.TryRemove(testRunId, out _);
        AlwaysFailNames.TryRemove(testRunId, out _);
    }
}

[Trait("Category", "Integration")]
public class RetryIntegrationTests : IDisposable
{
    private readonly string _testRunId = Guid.NewGuid().ToString("N");

    public RetryIntegrationTests()
    {
        RetryTestTracker.CurrentTestRunId = _testRunId;
    }

    public void Dispose()
    {
        RetryTestTracker.Reset(_testRunId);
    }

    private ServiceProvider BuildProvider(Action<BulkSharpOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());

            if (configureOptions != null)
                builder.ConfigureOptions(configureOptions);
        });
        services.AddScoped<RetryableRowOperation>();
        services.AddScoped<RetryablePipelineOperation>();
        services.AddScoped<NonRetryableStepPipelineOperation>();
        services.AddLogging();

        // Register retry service and its dependencies (not yet in DI — Task 11)
        services.AddSingleton<IBulkRowRetryHistoryRepository, InMemoryBulkRowRetryHistoryRepository>();
        services.AddScoped<IBulkRetryService, BulkRetryService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FullRetryFlow_FailedRowsSucceedOnRetry_OperationCompletes()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        // Row 2 (bob) will fail on first attempt, succeed on retry
        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-row-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);
        Assert.Equal(3, operation.TotalRows);
        Assert.True(operation.FailedRows > 0);

        // Verify eligible for retry
        var eligibility = await retryService.CanRetryAsync(opId);
        Assert.True(eligibility.IsEligible);

        // Retry — bob succeeds this time because the tracker recorded the first attempt
        await retryService.RetryFailedRowsAsync(opId, new RetryRequest());

        // ImmediateScheduler processes inline, so the operation is already retried
        operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.Completed, operation!.Status);
        Assert.Equal(3, operation.SuccessfulRows);
        Assert.Equal(0, operation.FailedRows);
    }

    [Fact]
    public async Task PartialRetry_OnlySpecifiedRowsAreRetried()
    {
        // Make both bob and charlie fail on first attempt
        RetryTestTracker.AlwaysFailNames.GetOrAdd(_testRunId, _ => new ConcurrentBag<string>()).Add("charlie");

        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-row-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);
        Assert.Equal(2, operation.FailedRows); // bob and charlie both failed

        // Only retry row 2 (bob), not row 3 (charlie)
        var submission = await retryService.RetryFailedRowsAsync(opId, new RetryRequest { RowNumbers = [2] });
        Assert.Equal(1, submission.RowsSubmitted);

        operation = await operationService.GetBulkOperationAsync(opId);
        // Should be CompletedWithErrors since charlie (row 3) still failed
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);
    }

    [Fact]
    public async Task RetryFromMidStep_PipelineResumesFromFailedStep()
    {
        var tracker = RetryTestTracker.GetTracker();

        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        // bob fails at step 2 (Enrich) on first attempt
        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-pipeline-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);

        // Track how many times step 1 (Transform) was called for bob before retry
        var bobTransformBefore = tracker.GetValueOrDefault($"bob-Transform", 0);

        // Retry
        await retryService.RetryFailedRowsAsync(opId, new RetryRequest());

        // Step 1 (Transform) should NOT have been re-executed for bob
        var bobTransformAfter = tracker.GetValueOrDefault($"bob-Transform", 0);
        Assert.Equal(bobTransformBefore, bobTransformAfter);

        // Step 2 (Enrich) should have been re-executed
        var bobEnrichCount = tracker.GetValueOrDefault($"bob-Enrich", 0);
        Assert.True(bobEnrichCount >= 2, "Enrich step should have been called at least twice (original + retry)");

        operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.Completed, operation!.Status);
    }

    [Fact]
    public async Task NonRetryableStep_RowIsSkippedInRetry()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        // bob fails at the FinalizeNoRetry step (AllowOperationRetry = false)
        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("non-retryable-step-pipeline-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);

        // Retry — the row should be skipped because it failed in a non-retryable step
        var submission = await retryService.RetryFailedRowsAsync(opId, new RetryRequest());
        Assert.Equal(0, submission.RowsSubmitted);
        Assert.True(submission.RowsSkipped > 0);
        Assert.NotNull(submission.SkippedReasons);
        Assert.Contains(submission.SkippedReasons, r => r.Contains("does not allow operation retry"));
    }

    [Fact]
    public async Task ValidationFailedRows_AreNotRetried()
    {
        // charlie will always fail during processing
        RetryTestTracker.AlwaysFailNames.GetOrAdd(_testRunId, _ => new ConcurrentBag<string>()).Add("charlie");

        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var rowRecordRepo = provider.GetRequiredService<IBulkRowRecordRepository>();

        // Row 2 has invalid email (validation failure, StepIndex == -1)
        // Row 3 (charlie) will fail during processing (StepIndex >= 0)
        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,invalid-email,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-row-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);

        // Only charlie (processing failure) should be retried, not bob (validation failure)
        var submission = await retryService.RetryFailedRowsAsync(opId, new RetryRequest());
        Assert.Equal(1, submission.RowsSubmitted); // Only charlie

        // Verify bob's validation record still has StepIndex -1
        var bobRecords = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
        {
            OperationId = opId,
            RowNumber = 2,
            PageSize = 100
        });
        var bobValidation = bobRecords.Items.FirstOrDefault(r => r.StepIndex == -1);
        Assert.NotNull(bobValidation);
        Assert.Equal(RowRecordState.Failed, bobValidation.State);
    }

    [Fact]
    public async Task RetryHistoryPreserved_AfterRetry()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var retryHistoryRepo = provider.GetRequiredService<IBulkRowRetryHistoryRepository>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-row-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        await retryService.RetryFailedRowsAsync(opId, new RetryRequest());

        // Query retry history
        var history = await retryHistoryRepo.QueryAsync(new BulkRowRetryHistoryQuery
        {
            OperationId = opId,
            PageSize = 100
        });

        Assert.True(history.Items.Count > 0, "Retry history should exist");

        // bob (row 2) should have history with the original error
        var bobHistory = history.Items.FirstOrDefault(h => h.RowNumber == 2);
        Assert.NotNull(bobHistory);
        Assert.NotNull(bobHistory.ErrorMessage);
        Assert.True(bobHistory.Attempt >= 0);
    }

    [Fact]
    public async Task MaxRetryAttempts_BlocksRetryAfterLimitReached()
    {
        // Make bob always fail
        RetryTestTracker.AlwaysFailNames.GetOrAdd(_testRunId, _ => new ConcurrentBag<string>()).Add("bob");

        var provider = BuildProvider(opts => opts.MaxRetryAttempts = 1);
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-row-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);

        // First retry attempt
        await retryService.RetryFailedRowsAsync(opId, new RetryRequest());

        // After retry, bob still fails so operation is CompletedWithErrors
        operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);
        Assert.Equal(1, operation.RetryCount);

        // Second retry should be blocked
        var eligibility = await retryService.CanRetryAsync(opId);
        Assert.False(eligibility.IsEligible);
        Assert.Contains("Maximum retry attempts", eligibility.Reason);
    }

    [Fact]
    public async Task RetryFromStepIndex_ClearedAfterRetryCompletes()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var retryService = provider.GetRequiredService<IBulkRetryService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var rowRecordRepo = provider.GetRequiredService<IBulkRowRecordRepository>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new RetryMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("retryable-pipeline-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        await retryService.RetryFailedRowsAsync(opId, new RetryRequest());

        // Check that RetryFromStepIndex is null on all row records after retry
        var allRecords = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
        {
            OperationId = opId,
            PageSize = 1000
        });

        foreach (var record in allRecords.Items.Where(r => r.StepIndex >= 0))
        {
            Assert.Null(record.RetryFromStepIndex);
        }
    }
}

// --- Test metadata and row types ---

public class RetryMetadata : IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
}

[CsvSchema("1.0")]
public class RetryCsvRow : IBulkRow
{
    [CsvColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [CsvColumn("Email")]
    public string Email { get; set; } = string.Empty;

    [CsvColumn("Age")]
    public int Age { get; set; }

    public string? RowId { get; set; }
}

// --- Simple retryable row operation ---
// bob (row 2) fails on first process attempt, succeeds on retry

[BulkOperation("retryable-row-op", TrackRowData = true, IsRetryable = true)]
public class RetryableRowOperation : IBulkRowOperation<RetryMetadata, RetryCsvRow>
{
    public bool IsRetryable => true;

    public Task ValidateMetadataAsync(RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        var tracker = RetryTestTracker.GetTracker();
        var key = $"{row.Name}-process";
        var attempt = tracker.AddOrUpdate(key, 1, (_, v) => v + 1);

        // Check if this name should always fail
        var testRunId = RetryTestTracker.CurrentTestRunId ?? "default";
        if (RetryTestTracker.AlwaysFailNames.TryGetValue(testRunId, out var alwaysFail) && alwaysFail.Contains(row.Name))
            throw new InvalidOperationException($"Processing failed for {row.Name} (always fail)");

        // bob fails on first attempt, succeeds on subsequent
        if (row.Name == "bob" && attempt == 1)
            throw new InvalidOperationException($"Processing failed for {row.Name}");

        return Task.CompletedTask;
    }
}

// --- Pipeline operation: 3 steps, bob fails at step 2 (Enrich) on first attempt ---

[BulkOperation("retryable-pipeline-op", TrackRowData = true, IsRetryable = true)]
public class RetryablePipelineOperation : IBulkPipelineOperation<RetryMetadata, RetryCsvRow>
{
    public bool IsRetryable => true;

    public Task ValidateMetadataAsync(RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }

    [BulkStep("Transform", Order = 1)]
    public Task TransformAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken)
    {
        var tracker = RetryTestTracker.GetTracker();
        tracker.AddOrUpdate($"{row.Name}-Transform", 1, (_, v) => v + 1);
        return Task.CompletedTask;
    }

    [BulkStep("Enrich", Order = 2)]
    public Task EnrichAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken)
    {
        var tracker = RetryTestTracker.GetTracker();
        var key = $"{row.Name}-Enrich";
        var attempt = tracker.AddOrUpdate(key, 1, (_, v) => v + 1);

        if (row.Name == "bob" && attempt == 1)
            throw new InvalidOperationException($"Enrich failed for {row.Name}");

        return Task.CompletedTask;
    }

    [BulkStep("Finalize", Order = 3)]
    public Task FinalizeAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken)
    {
        var tracker = RetryTestTracker.GetTracker();
        tracker.AddOrUpdate($"{row.Name}-Finalize", 1, (_, v) => v + 1);
        return Task.CompletedTask;
    }
}

// --- Pipeline operation with a non-retryable step ---

[BulkOperation("non-retryable-step-pipeline-op", TrackRowData = true, IsRetryable = true)]
public class NonRetryableStepPipelineOperation : IBulkPipelineOperation<RetryMetadata, RetryCsvRow>
{
    public bool IsRetryable => true;

    public Task ValidateMetadataAsync(RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }

    [BulkStep("Prepare", Order = 1)]
    public Task PrepareAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [BulkStep("FinalizeNoRetry", Order = 2, AllowOperationRetry = false)]
    public Task FinalizeNoRetryAsync(RetryCsvRow row, RetryMetadata metadata, CancellationToken cancellationToken)
    {
        var tracker = RetryTestTracker.GetTracker();
        var key = $"{row.Name}-FinalizeNoRetry";
        var attempt = tracker.AddOrUpdate(key, 1, (_, v) => v + 1);

        if (row.Name == "bob" && attempt == 1)
            throw new InvalidOperationException($"FinalizeNoRetry failed for {row.Name}");

        return Task.CompletedTask;
    }
}
