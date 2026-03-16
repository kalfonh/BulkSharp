using System.ComponentModel.DataAnnotations;
using System.Reflection;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Configuration;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSharp.Dashboard;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Configures the BulkSharp Dashboard middleware, API endpoints, and Blazor hub.
    /// Use <paramref name="configureAdditionalEndpoints"/> to register extra endpoints
    /// (e.g., sample data runners) before the Blazor fallback route.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="configureAdditionalEndpoints">Optional callback to register extra endpoints before the Blazor fallback route.</param>
    /// <param name="authorizationPolicy">
    /// Optional authorization policy name applied to mutating endpoints (create, cancel, signal).
    /// When null, no authorization is enforced — the host application must configure its own middleware.
    /// </param>
    public static WebApplication UseBulkSharpDashboard(
        this WebApplication app,
        Action<WebApplication>? configureAdditionalEndpoints = null,
        string? authorizationPolicy = null)
    {
        app.UseStaticFiles();
        app.UseRouting();

        app.MapGet("/api/operations", (IBulkOperationDiscovery discovery) =>
        {
            var operations = discovery.DiscoverOperations();
            return operations.Select(op => new
            {
                op.Name,
                op.Description,
                op.IsStepBased,
                MetadataType = op.MetadataType?.Name,
                RowType = op.RowType?.Name,
                TypeFullName = op.OperationType?.FullName,
                MetadataFields = op.MetadataType?.GetProperties()
                    .Where(p => p.CanWrite)
                    .Select(p => new
                    {
                        p.Name,
                        Type = GetFriendlyTypeName(p.PropertyType),
                        Required = p.GetCustomAttribute<RequiredAttribute>() != null
                    }),
                FileColumns = op.RowType?.GetProperties()
                    .Where(p => p.CanWrite)
                    .Select(p => new
                    {
                        Name = p.GetCustomAttribute<CsvColumnAttribute>()?.Name ?? p.Name,
                        Type = GetFriendlyTypeName(p.PropertyType),
                        Required = p.GetCustomAttribute<CsvColumnAttribute>()?.Required ?? false
                    })
            });
        });

        app.MapGet("/api/operations/{name}/template", (
            string name,
            IBulkOperationDiscovery discovery) =>
        {
            var opInfo = discovery.GetOperation(name);
            if (opInfo == null)
                return Results.NotFound($"Operation '{name}' not found");

            var rowType = opInfo.RowType;
            var columns = rowType.GetProperties()
                .Where(p => p.CanWrite)
                .Select(p =>
                {
                    var csvAttr = p.GetCustomAttribute<CsvColumnAttribute>();
                    return csvAttr?.Name ?? p.Name;
                })
                .ToList();

            var csv = string.Join(",", columns) + "\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            return Results.File(bytes, "text/csv", $"{name}-template.csv");
        });

        app.MapGet("/api/bulks", async (
            [FromServices] IBulkOperationService service,
            [FromQuery] string? operationName,
            [FromQuery] string? createdBy,
            [FromQuery] BulkOperationStatus? status,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = null,
            [FromQuery] bool sortDescending = true,
            CancellationToken cancellationToken = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var query = new BulkOperationQuery
            {
                OperationName = operationName,
                CreatedBy = createdBy,
                Status = status,
                FromDate = fromDate,
                ToDate = toDate,
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDescending = sortDescending
            };
            return await service.QueryBulkOperationsAsync(query, cancellationToken);
        });

        app.MapGet("/api/bulks/{id}", async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            var bulk = await service.GetBulkOperationAsync(id, cancellationToken);
            return bulk is not null ? Results.Ok(bulk) : Results.NotFound();
        });

        app.MapGet("/api/bulks/{id}/errors", async (
            Guid id,
            IBulkRowRecordRepository rowRecordRepo,
            [FromQuery] int? rowNumber,
            [FromQuery] string? rowId,
            [FromQuery] string? errorType,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = null,
            [FromQuery] bool sortDescending = false,
            CancellationToken cancellationToken = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);

            BulkErrorType? parsedErrorType = null;
            if (!string.IsNullOrEmpty(errorType) && Enum.TryParse<BulkErrorType>(errorType, true, out var et))
                parsedErrorType = et;

            var query = new BulkRowRecordQuery
            {
                OperationId = id,
                RowNumber = rowNumber,
                RowId = rowId,
                ErrorType = parsedErrorType,
                ErrorsOnly = true,
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy ?? "RowNumber",
                SortDescending = sortDescending
            };

            var result = await rowRecordRepo.QueryAsync(query, cancellationToken);

            var items = result.Items.Select(r => new
            {
                r.Id,
                r.BulkOperationId,
                r.RowNumber,
                r.RowId,
                ErrorType = r.ErrorType?.ToString() ?? "Unknown",
                r.ErrorMessage,
                RowData = r.RowData,
                r.CreatedAt
            });

            return Results.Ok(new
            {
                Items = items,
                result.TotalCount,
                result.Page,
                result.PageSize,
                result.HasNextPage
            });
        });

        app.MapGet("/api/bulks/{id}/status", async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            var bulk = await service.GetBulkOperationAsync(id, cancellationToken);
            if (bulk == null)
                return Results.NotFound();

            return Results.Ok(new
            {
                bulk.Status,
                bulk.ProcessedRows,
                bulk.TotalRows,
                bulk.ErrorCount,
                bulk.CompletedAt,
                Progress = bulk.TotalRows > 0 ? (bulk.ProcessedRows * 100.0 / bulk.TotalRows) : 0
            });
        });

        var cancelEndpoint = app.MapPost("/api/bulks/{id}/cancel", async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            await service.CancelBulkOperationAsync(id, cancellationToken);
            return Results.Ok();
        });
        if (authorizationPolicy != null) cancelEndpoint.RequireAuthorization(authorizationPolicy);

        app.MapGet("/api/bulks/{id:guid}/rows", async (
            Guid id,
            IBulkRowRecordRepository repo,
            [FromQuery] string? rowId,
            [FromQuery] string? state,
            [FromQuery] string? stepName,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            CancellationToken cancellationToken = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);

            RowRecordState? parsedState = null;
            if (!string.IsNullOrEmpty(state) && Enum.TryParse<RowRecordState>(state, true, out var s))
                parsedState = s;

            // Apply filters if provided
            IReadOnlyList<int>? filteredRowNumbers = null;
            if (parsedState.HasValue || !string.IsNullOrEmpty(stepName) || !string.IsNullOrEmpty(rowId))
            {
                var filterQuery = new BulkRowRecordQuery
                {
                    OperationId = id,
                    State = parsedState,
                    StepName = stepName,
                    RowId = rowId,
                    PageSize = 10000
                };
                var filtered = await repo.QueryAsync(filterQuery, cancellationToken);
                filteredRowNumbers = filtered.Items.Select(r => r.RowNumber).Distinct().ToList();

                if (filteredRowNumbers.Count == 0)
                    return Results.Ok(new { Items = Array.Empty<object>(), TotalCount = 0, Page = page, PageSize = pageSize, HasNextPage = false });
            }

            var rowNumbersPage = filteredRowNumbers != null
                ? new PagedResult<int>
                {
                    Items = filteredRowNumbers.OrderBy(n => n).Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                    TotalCount = filteredRowNumbers.Count,
                    Page = page,
                    PageSize = pageSize
                }
                : await repo.QueryDistinctRowNumbersAsync(id, page, pageSize, cancellationToken);

            if (rowNumbersPage.Items.Count == 0)
                return Results.Ok(new { Items = Array.Empty<object>(), rowNumbersPage.TotalCount, rowNumbersPage.Page, rowNumbersPage.PageSize, rowNumbersPage.HasNextPage });

            // Fetch all records for these rows (all step indexes including -1)
            var recordResult = await repo.QueryAsync(new BulkRowRecordQuery
            {
                OperationId = id,
                RowNumbers = rowNumbersPage.Items,
                Page = 1,
                PageSize = Math.Min(rowNumbersPage.Items.Count * 50, 5000)
            }, cancellationToken);

            var rows = recordResult.Items
                .GroupBy(r => r.RowNumber)
                .Select(g =>
                {
                    var executionSteps = g.Where(r => r.StepIndex >= 0).ToList();
                    var validationRecord = g.FirstOrDefault(r => r.StepIndex == -1);

                    var activeStep = executionSteps
                        .Where(r => r.State is RowRecordState.Running or RowRecordState.WaitingForCompletion)
                        .Select(r => r.StepName)
                        .FirstOrDefault();

                    var latestNonPending = executionSteps
                        .OrderByDescending(r => r.StepIndex)
                        .FirstOrDefault(r => r.State != RowRecordState.Pending);

                    var currentStep = activeStep
                        ?? latestNonPending?.StepName
                        ?? validationRecord?.StepName
                        ?? "Unknown";

                    var currentState = latestNonPending?.State.ToString()
                        ?? validationRecord?.State.ToString()
                        ?? RowRecordState.Pending.ToString();

                    return new
                    {
                        RowNumber = g.Key,
                        RowId = g.First().RowId,
                        CurrentStep = currentStep,
                        CurrentState = currentState,
                        CompletedSteps = executionSteps.Count(r => r.State == RowRecordState.Completed),
                        TotalSteps = Math.Max(executionSteps.Count, 1),
                        Steps = executionSteps.OrderBy(r => r.StepIndex).Select(r => new
                        {
                            r.StepName,
                            State = r.State.ToString(),
                            r.SignalKey,
                            r.StartedAt,
                            r.CompletedAt,
                            r.ErrorMessage
                        })
                    };
                })
                .OrderBy(r => r.RowNumber);

            return Results.Ok(new
            {
                Items = rows.ToList(),
                rowNumbersPage.TotalCount,
                rowNumbersPage.Page,
                rowNumbersPage.PageSize,
                rowNumbersPage.HasNextPage
            });
        });

        var signalEndpoint = app.MapPost("/api/bulks/{id:guid}/signal/{key}", async (
            Guid id,
            string key,
            [FromServices] IBulkRowRecordRepository recordRepo,
            [FromServices] IBulkStepSignalService signalService,
            CancellationToken cancellationToken) =>
        {
            var scopedKeyPrefix = $"{id}:{key}:";
            var waitingRecords = await recordRepo.QueryAsync(new BulkRowRecordQuery
            {
                OperationId = id,
                State = RowRecordState.WaitingForCompletion,
                PageSize = 1000
            }, cancellationToken);

            var record = waitingRecords.Items
                .FirstOrDefault(r => r.SignalKey != null && r.SignalKey.StartsWith(scopedKeyPrefix, StringComparison.Ordinal));

            if (record == null)
                return Results.NotFound(new { error = $"No waiting step found for signal key '{key}'" });

            // Try in-process signal first (same-process scenario)
            if (signalService.TrySignal(record.SignalKey!))
                return Results.Ok(new { completed = true, rowNumber = record.RowNumber, step = record.StepName });

            // Cross-process: write completion to DB. Worker will pick it up via polling.
            record.MarkCompleted();
            await recordRepo.UpdateAsync(record, cancellationToken);
            return Results.Ok(new { completed = true, rowNumber = record.RowNumber, step = record.StepName, crossProcess = true });
        });
        if (authorizationPolicy != null) signalEndpoint.RequireAuthorization(authorizationPolicy);

        var signalFailEndpoint = app.MapPost("/api/bulks/{id:guid}/signal/{key}/fail", async (
            Guid id,
            string key,
            [FromBody] SignalFailureRequest request,
            [FromServices] IBulkRowRecordRepository recordRepo,
            [FromServices] IBulkStepSignalService signalService,
            CancellationToken cancellationToken) =>
        {
            var scopedKeyPrefix = $"{id}:{key}:";
            var waitingRecords = await recordRepo.QueryAsync(new BulkRowRecordQuery
            {
                OperationId = id,
                State = RowRecordState.WaitingForCompletion,
                PageSize = 1000
            }, cancellationToken);

            var record = waitingRecords.Items
                .FirstOrDefault(r => r.SignalKey != null && r.SignalKey.StartsWith(scopedKeyPrefix, StringComparison.Ordinal));

            if (record == null)
                return Results.NotFound(new { error = $"No waiting step found for signal key '{key}'" });

            var errorMessage = request.ErrorMessage?.Length > 2000
                ? request.ErrorMessage[..2000]
                : request.ErrorMessage ?? string.Empty;

            // Try in-process signal first (same-process scenario)
            if (signalService.TrySignalFailure(record.SignalKey!, errorMessage))
                return Results.Ok(new { failed = true, rowNumber = record.RowNumber, step = record.StepName, error = errorMessage });

            // Cross-process: write failure to DB. Worker will pick it up via polling.
            record.MarkFailed(errorMessage, BulkErrorType.SignalFailure);
            await recordRepo.UpdateAsync(record, cancellationToken);
            return Results.Ok(new { failed = true, rowNumber = record.RowNumber, step = record.StepName, error = errorMessage, crossProcess = true });
        });
        if (authorizationPolicy != null) signalFailEndpoint.RequireAuthorization(authorizationPolicy);

        app.MapPost("/api/bulks/validate", async (
            HttpRequest request,
            [FromServices] IBulkOperationService service,
            [FromServices] IOptions<BulkSharpOptions> options,
            CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.FirstOrDefault();
            var operationName = form["operationName"].ToString();
            var metadataJson = form["metadata"].ToString();

            if (file != null && options.Value.MaxFileSizeBytes > 0 && file.Length > options.Value.MaxFileSizeBytes)
                return Results.BadRequest($"File exceeds maximum allowed size of {options.Value.MaxFileSizeBytes} bytes");

            if (string.IsNullOrEmpty(operationName))
                return Results.BadRequest(new { error = "Operation name is required" });

            using var stream = file?.OpenReadStream() ?? Stream.Null;
            var fileName = file?.FileName ?? "";

            var result = await service.ValidateBulkOperationAsync(
                operationName, metadataJson, stream, fileName, cancellationToken);

            return result.IsValid
                ? Results.Ok(new { valid = true })
                : Results.Ok(new { valid = false, result.MetadataErrors, result.FileErrors });
        });

        app.MapGet("/api/bulks/{id:guid}/file", async (
            Guid id,
            [FromServices] IBulkOperationService operationService,
            [FromServices] IManagedStorageProvider storageProvider,
            CancellationToken cancellationToken) =>
        {
            var operation = await operationService.GetBulkOperationAsync(id, cancellationToken).ConfigureAwait(false);
            if (operation == null || operation.FileId == Guid.Empty)
                return Results.NotFound();

            var fileInfo = await storageProvider.GetFileInfoAsync(operation.FileId, cancellationToken).ConfigureAwait(false);
            if (fileInfo == null)
                return Results.NotFound();

            var stream = await storageProvider.RetrieveFileAsync(operation.FileId, cancellationToken).ConfigureAwait(false);
            return Results.File(stream, fileInfo.ContentType, operation.FileName);
        });

        var createEndpoint = app.MapPost("/api/bulks", async (
            HttpRequest request,
            [FromServices] IBulkOperationService operationService,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Form data required");

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            var operationName = form["operationName"].ToString();
            var createdBy = form["createdBy"].ToString();
            var metadataJson = form["metadata"].ToString();

            if (file == null || file.Length == 0)
                return Results.BadRequest("File is required");

            if (string.IsNullOrWhiteSpace(operationName))
                return Results.BadRequest("Operation name is required");

            if (string.IsNullOrWhiteSpace(createdBy))
                return Results.BadRequest("Created by is required");

            if (operationName.Length > 200)
                return Results.BadRequest("Operation name must not exceed 200 characters");

            if (createdBy.Length > 200)
                return Results.BadRequest("Created by must not exceed 200 characters");

            if (metadataJson.Length > 1_048_576)
                return Results.BadRequest("Metadata JSON must not exceed 1 MB");

            try
            {
                using var stream = file.OpenReadStream();
                var operationId = await operationService.CreateBulkOperationAsync(
                    operationName, stream, file.FileName, metadataJson ?? "{}", createdBy, cancellationToken);

                return Results.Ok(new { OperationId = operationId });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                var logger = loggerFactory.CreateLogger("BulkSharp.Dashboard.Api");
                logger.CreateOperationFailed(ex, operationName);
                return Results.StatusCode(500);
            }
        });
        if (authorizationPolicy != null) createEndpoint.RequireAuthorization(authorizationPolicy);

        // Let the host app register additional endpoints (e.g. sample data) before Blazor fallback
        configureAdditionalEndpoints?.Invoke(app);

        app.MapRazorPages();
        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");

        return app;
    }

    internal record SignalFailureRequest(string ErrorMessage);

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null) return GetFriendlyTypeName(underlying) + "?";

        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(DateTime)) return "datetime";
        if (type == typeof(DateTimeOffset)) return "datetimeoffset";
        if (type == typeof(Guid)) return "guid";
        return type.Name.ToLowerInvariant();
    }
}
