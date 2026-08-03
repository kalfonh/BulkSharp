using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Configuration;
using BulkSharp.Core.Domain.Export;
using BulkSharp.Core.Domain.Notifications;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Domain.Retry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSharp.Api;

/// <summary>
/// Maps the BulkSharp HTTP API onto an application.
/// </summary>
/// <remarks>
/// This package carries no UI. Reference <c>BulkSharp.Dashboard</c> as well if you want
/// the built-in Blazor dashboard; otherwise build your own front end against these
/// endpoints in whatever technology stack you prefer.
/// </remarks>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Serves the OpenAPI document describing the BulkSharp API at
    /// <c>/openapi/v1.json</c>, for generating clients in any technology stack.
    /// </summary>
    /// <remarks>
    /// Hosts typically map this outside production. The route matches the .NET 9
    /// built-in OpenAPI convention so the underlying generator can be swapped later
    /// without breaking client-generation pipelines.
    /// </remarks>
    /// <param name="app">The web application.</param>
    public static WebApplication MapBulkSharpOpenApi(this WebApplication app)
    {
        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
        return app;
    }

    /// <summary>
    /// Maps the BulkSharp API endpoints. Call <c>AddBulkSharpEndpoints()</c> during service
    /// registration so responses use the BulkSharp JSON contract.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="authorizationPolicy">
    /// Optional authorization policy name applied to mutating endpoints (create, cancel, signal, retry).
    /// When null, no authorization is enforced — the host application must configure its own middleware.
    /// </param>
    public static WebApplication MapBulkSharpEndpoints(
        this WebApplication app,
        string? authorizationPolicy = null)
    {
        app.MapGet(BulkSharpRoutes.Operations, (IBulkOperationDiscovery discovery) =>
            discovery.DiscoverOperations().Select(op => new OperationDescriptorDto
            {
                Name = op.Name,
                Description = op.Description,
                IsStepBased = op.IsStepBased,
                MetadataType = op.MetadataType?.Name,
                RowType = op.RowType?.Name,
                TypeFullName = op.OperationType?.FullName,
                MetadataFields = op.MetadataType?.GetProperties()
                    .Where(p => p.CanWrite)
                    .Select(p => new OperationFieldDto(
                        p.Name,
                        GetFriendlyTypeName(p.PropertyType),
                        p.GetCustomAttribute<RequiredAttribute>() != null))
                    .ToList() ?? [],
                FileColumns = op.RowType?.GetProperties()
                    .Where(p => p.CanWrite)
                    .Select(p => new OperationFieldDto(
                        p.GetCustomAttribute<CsvColumnAttribute>()?.Name ?? p.Name,
                        GetFriendlyTypeName(p.PropertyType),
                        p.GetCustomAttribute<CsvColumnAttribute>()?.Required ?? false))
                    .ToList() ?? []
            }).ToList())
            .WithName("getOperations")
            .WithSummary("Lists registered operations with their metadata fields and file columns.")
            .Produces<IReadOnlyList<OperationDescriptorDto>>();

        app.MapGet(BulkSharpRoutes.OperationTemplate, (
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
        })
            .WithName("getOperationTemplate")
            .WithSummary("Downloads a CSV template with the operation's expected header row.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet(BulkSharpRoutes.Bulks, async (
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
        })
            .WithName("getBulks")
            .WithSummary("Queries bulk operations with filtering, sorting and paging.")
            .Produces<PagedResult<BulkOperation>>();

        app.MapGet(BulkSharpRoutes.Bulk, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            var bulk = await service.GetBulkOperationAsync(id, cancellationToken);
            return bulk is not null ? Results.Ok(bulk) : Results.NotFound();
        })
            .WithName("getBulk")
            .WithSummary("Returns a single bulk operation.")
            .Produces<BulkOperation>()
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet(BulkSharpRoutes.BulkErrors, async (
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

            return Results.Ok(new PagedResult<RowErrorDto>
            {
                Items = result.Items.Select(r => new RowErrorDto(
                    r.Id,
                    r.BulkOperationId,
                    r.RowNumber,
                    r.RowId,
                    r.ErrorType,
                    r.ErrorMessage,
                    r.RowData,
                    r.CreatedAt)).ToList(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize
            });
        })
            .WithName("getBulkErrors")
            .WithSummary("Returns the failed rows of an operation, paged and filterable.")
            .Produces<PagedResult<RowErrorDto>>();

        app.MapGet(BulkSharpRoutes.BulkStatus, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            var bulk = await service.GetBulkOperationAsync(id, cancellationToken);
            if (bulk == null)
                return Results.NotFound();

            return Results.Ok(new BulkStatusDto(
                bulk.Status,
                bulk.ProcessedRows,
                bulk.TotalRows,
                bulk.ErrorCount,
                bulk.CompletedAt,
                bulk.TotalRows > 0 ? bulk.ProcessedRows * 100.0 / bulk.TotalRows : 0));
        })
            .WithName("getBulkStatus")
            .WithSummary("Returns a lightweight progress snapshot, suitable for polling.")
            .Produces<BulkStatusDto>()
            .Produces(StatusCodes.Status404NotFound);

        var cancelEndpoint = app.MapPost(BulkSharpRoutes.BulkCancel, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            await service.CancelBulkOperationAsync(id, cancellationToken);
            return Results.Ok();
        })
            .WithName("cancelBulk")
            .WithSummary("Cancels a pending or running operation.")
            .Produces(StatusCodes.Status200OK);
        if (authorizationPolicy != null) cancelEndpoint.RequireAuthorization(authorizationPolicy);

        app.MapGet(BulkSharpRoutes.BulkRows, async (
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
                    return Results.Ok(new PagedResult<RowProgressDto> { Page = page, PageSize = pageSize });
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
                return Results.Ok(new PagedResult<RowProgressDto>
                {
                    TotalCount = rowNumbersPage.TotalCount,
                    Page = rowNumbersPage.Page,
                    PageSize = rowNumbersPage.PageSize
                });

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

                    var currentState = latestNonPending?.State
                        ?? validationRecord?.State
                        ?? RowRecordState.Pending;

                    return new RowProgressDto(
                        g.Key,
                        g.First().RowId,
                        currentStep,
                        currentState,
                        executionSteps.Count(r => r.State == RowRecordState.Completed),
                        Math.Max(executionSteps.Count, 1),
                        executionSteps.OrderBy(r => r.StepIndex).Select(r => new RowStepDto(
                            r.StepName,
                            r.State,
                            r.SignalKey,
                            r.StartedAt,
                            r.CompletedAt,
                            r.ErrorMessage)).ToList());
                })
                .OrderBy(r => r.RowNumber);

            return Results.Ok(new PagedResult<RowProgressDto>
            {
                Items = rows.ToList(),
                TotalCount = rowNumbersPage.TotalCount,
                Page = rowNumbersPage.Page,
                PageSize = rowNumbersPage.PageSize
            });
        })
            .WithName("getBulkRows")
            .WithSummary("Returns per-row pipeline progress with per-step detail, paged.")
            .Produces<PagedResult<RowProgressDto>>();

        var signalEndpoint = app.MapPost(BulkSharpRoutes.BulkSignal, async (
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
                return Results.Ok(new SignalResponse(
                    record.RowNumber, record.StepName, Completed: true, Failed: false, Error: null, CrossProcess: false));

            // Cross-process: write completion to DB. Worker will pick it up via polling.
            record.MarkCompleted();
            await recordRepo.UpdateAsync(record, cancellationToken);
            return Results.Ok(new SignalResponse(
                record.RowNumber, record.StepName, Completed: true, Failed: false, Error: null, CrossProcess: true));
        })
            .WithName("signalStep")
            .WithSummary("Completes a pipeline step that is waiting on an external signal.")
            .Produces<SignalResponse>()
            .Produces(StatusCodes.Status404NotFound);
        if (authorizationPolicy != null) signalEndpoint.RequireAuthorization(authorizationPolicy);

        var signalFailEndpoint = app.MapPost(BulkSharpRoutes.BulkSignalFail, async (
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
                return Results.Ok(new SignalResponse(
                    record.RowNumber, record.StepName, Completed: false, Failed: true, errorMessage, CrossProcess: false));

            // Cross-process: write failure to DB. Worker will pick it up via polling.
            record.MarkFailed(errorMessage, BulkErrorType.SignalFailure);
            await recordRepo.UpdateAsync(record, cancellationToken);
            return Results.Ok(new SignalResponse(
                record.RowNumber, record.StepName, Completed: false, Failed: true, errorMessage, CrossProcess: true));
        })
            .WithName("failStep")
            .WithSummary("Fails a pipeline step that is waiting on an external signal.")
            .Produces<SignalResponse>()
            .Produces(StatusCodes.Status404NotFound);
        if (authorizationPolicy != null) signalFailEndpoint.RequireAuthorization(authorizationPolicy);

        app.MapPost(BulkSharpRoutes.BulksValidate, async (
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
                ? Results.Ok(new ValidationResponse(true, [], []))
                : Results.Ok(new ValidationResponse(false, result.MetadataErrors, result.FileErrors));
        })
            .WithName("validateBulk")
            .WithSummary("Validates a submission without creating an operation. Required pre-flight for generated forms, because operations may enforce rules the discovery descriptor cannot express.")
            .Produces<ValidationResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        app.MapGet(BulkSharpRoutes.BulkFile, async (
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
        })
            .WithName("getBulkFile")
            .WithSummary("Downloads the source file the operation was created from.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound);

        var createEndpoint = app.MapPost(BulkSharpRoutes.Bulks, async (
            HttpRequest request,
            HttpContext context,
            [FromServices] IBulkOperationService operationService,
            [FromServices] IBulkUserResolver userResolver,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Form data required");

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            var operationName = form["operationName"].ToString();

            // Attribution comes from the authenticated principal when there is one. The
            // form value is only honoured for anonymous self-hosting; otherwise any caller
            // could attribute an operation to any user and the audit trail would be fiction.
            var createdBy = userResolver.ResolveUser(context.User) ?? form["createdBy"].ToString();
            var metadataJson = form["metadata"].ToString();
            var notificationsJson = form["notifications"].ToString();

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
                NotificationOptions? notifications = null;
                if (!string.IsNullOrWhiteSpace(notificationsJson))
                {
                    try
                    {
                        notifications = JsonSerializer.Deserialize<NotificationOptions>(notificationsJson);
                    }
                    catch (JsonException)
                    {
                        return Results.BadRequest("Invalid notifications JSON");
                    }
                }

                using var stream = file.OpenReadStream();
                var operationId = await operationService.CreateBulkOperationAsync(
                    operationName, stream, file.FileName, metadataJson ?? "{}", createdBy, notifications, cancellationToken);

                return Results.Ok(new CreateOperationResponse(operationId));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                var logger = loggerFactory.CreateLogger("BulkSharp.Api");
                logger.CreateOperationFailed(ex, operationName);
                return Results.StatusCode(500);
            }
        })
            .WithName("createBulk")
            .WithSummary("Creates a bulk operation from an uploaded file.")
            .Produces<CreateOperationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError);
        if (authorizationPolicy != null) createEndpoint.RequireAuthorization(authorizationPolicy);

        // â”€â”€ Retry endpoints â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        var retryAllEndpoint = app.MapPost(BulkSharpRoutes.BulkRetry, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.RetryFailedRowsAsync(id, new RetryRequest(), cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
            .WithName("retryBulk")
            .WithSummary("Retries every failed row of an operation.")
            .Produces<RetrySubmission>()
            .Produces(StatusCodes.Status400BadRequest);
        if (authorizationPolicy != null) retryAllEndpoint.RequireAuthorization(authorizationPolicy);

        var retryRowsEndpoint = app.MapPost(BulkSharpRoutes.BulkRetryRows, async (
            Guid id,
            [FromBody] RetryRowsRequest request,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.RetryFailedRowsAsync(id,
                    new RetryRequest { RowNumbers = request.RowNumbers }, cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
            .WithName("retryBulkRows")
            .WithSummary("Retries a specific set of failed rows.")
            .Produces<RetrySubmission>()
            .Produces(StatusCodes.Status400BadRequest);
        if (authorizationPolicy != null) retryRowsEndpoint.RequireAuthorization(authorizationPolicy);

        app.MapGet(BulkSharpRoutes.BulkRetryEligibility, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CanRetryAsync(id, cancellationToken);
            return Results.Ok(result);
        })
            .WithName("getRetryEligibility")
            .WithSummary("Reports whether an operation can be retried, and why not when it cannot.")
            .Produces<RetryEligibility>();

        app.MapGet(BulkSharpRoutes.BulkRetryHistory, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            [FromQuery] int? rowNumber,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            CancellationToken cancellationToken = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var result = await service.QueryRetryHistoryAsync(new BulkRowRetryHistoryQuery
            {
                OperationId = id,
                RowNumber = rowNumber,
                Page = page,
                PageSize = pageSize
            }, cancellationToken);
            return Results.Ok(result);
        })
            .WithName("getRetryHistory")
            .WithSummary("Returns the retry attempts recorded for an operation, paged.")
            .Produces<PagedResult<BulkRowRetryHistory>>();

        // â”€â”€ Export endpoint â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        app.MapGet(BulkSharpRoutes.BulkExport, async (
            Guid id,
            [FromServices] IBulkOperationService service,
            [FromQuery] string mode = "report",
            [FromQuery] string format = "csv",
            [FromQuery] string? state = null,
            [FromQuery] string? errorType = null,
            [FromQuery] string? stepName = null,
            CancellationToken cancellationToken = default) =>
        {
            var exportMode = Enum.TryParse<ExportMode>(mode, true, out var m) ? m : ExportMode.Report;
            var exportFormat = Enum.TryParse<ExportFormat>(format, true, out var f) ? f : ExportFormat.Csv;

            RowRecordState? parsedState = null;
            if (!string.IsNullOrEmpty(state) && Enum.TryParse<RowRecordState>(state, true, out var s))
                parsedState = s;

            BulkErrorType? parsedErrorType = null;
            if (!string.IsNullOrEmpty(errorType) && Enum.TryParse<BulkErrorType>(errorType, true, out var et))
                parsedErrorType = et;

            try
            {
                var result = await service.ExportAsync(id, new ExportRequest
                {
                    Mode = exportMode,
                    Format = exportFormat,
                    State = parsedState,
                    ErrorType = parsedErrorType,
                    StepName = stepName
                }, cancellationToken);

                return Results.File(result.Stream, result.ContentType, result.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
            .WithName("exportBulk")
            .WithSummary("Exports a report, the failed rows, or the row detail of an operation.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    internal record SignalFailureRequest(string ErrorMessage);

    internal record RetryRowsRequest(IReadOnlyList<int> RowNumbers);

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
