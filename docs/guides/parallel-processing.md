# Parallel Row Processing

By default, BulkSharp processes rows sequentially (`MaxRowConcurrency = 1`). For I/O-bound operations, increase concurrency for better throughput.

## Configuration

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 8));
```

## How It Works

When `MaxRowConcurrency > 1`, BulkSharp uses a Channel-based producer-consumer pattern:

1. **Producer** - One thread streams rows from the file and writes them to a bounded channel
2. **Consumers** - A pool of `MaxRowConcurrency` workers read from the channel and process rows in parallel
3. **Progress tracking** - Row counts are updated via `Interlocked` operations (thread-safe)
4. **Error recording** - Errors are batched and flushed every `FlushBatchSize` rows

## When to Use

Parallel processing helps when row processing involves waiting:
- HTTP API calls
- Database writes
- Async steps (polling or signal-based)
- External service calls

It does **not** help (and may hurt) for CPU-bound processing since .NET already uses thread pool efficiently.

## Tuning

| Scenario | Recommended `MaxRowConcurrency` |
|----------|-------------------------------|
| CPU-bound processing | 1 (default) |
| API calls with rate limits | Match the rate limit |
| Database writes | 2-4 (avoid connection pool exhaustion) |
| Async steps with long waits | 10-50 (rows are mostly waiting, not computing) |

## Thread Safety

Your `ProcessRowAsync` and step `ExecuteAsync` implementations must be thread-safe when `MaxRowConcurrency > 1`. This means:
- No shared mutable state between rows
- Use thread-safe collections if accumulating results
- Database connections should be scoped per-row (BulkSharp handles this for its own repositories)
