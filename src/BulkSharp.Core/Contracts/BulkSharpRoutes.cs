namespace BulkSharp.Core.Contracts;

/// <summary>
/// The canonical BulkSharp HTTP route table.
/// </summary>
/// <remarks>
/// Every implementation of the BulkSharp contract maps these templates verbatim —
/// <c>BulkSharp.Api</c> serving a single backend, and <c>BulkSharp.Gateway</c> aggregating
/// several. Declaring the routes once means the two cannot drift apart by a typo or an
/// omission, and lets a single generated client work against either.
/// <para>
/// Adding a member here obliges both implementations to map it; a conformance test fails
/// otherwise. Adding a route to only one implementation fails the same test.
/// </para>
/// </remarks>
public static class BulkSharpRoutes
{
    /// <summary>Lists registered operations with their metadata fields and file columns.</summary>
    public const string Operations = "/api/operations";

    /// <summary>Downloads a CSV template for an operation.</summary>
    public const string OperationTemplate = "/api/operations/{name}/template";

    /// <summary>Queries operations (GET) and creates one (POST).</summary>
    public const string Bulks = "/api/bulks";

    /// <summary>Validates a submission without creating an operation.</summary>
    public const string BulksValidate = "/api/bulks/validate";

    /// <summary>A single operation.</summary>
    public const string Bulk = "/api/bulks/{id:guid}";

    /// <summary>Progress snapshot for polling.</summary>
    public const string BulkStatus = "/api/bulks/{id:guid}/status";

    /// <summary>Failed rows, paged.</summary>
    public const string BulkErrors = "/api/bulks/{id:guid}/errors";

    /// <summary>Per-row pipeline progress, paged.</summary>
    public const string BulkRows = "/api/bulks/{id:guid}/rows";

    /// <summary>Downloads the operation's source file.</summary>
    public const string BulkFile = "/api/bulks/{id:guid}/file";

    /// <summary>Exports a report, the failed rows, or row detail.</summary>
    public const string BulkExport = "/api/bulks/{id:guid}/export";

    /// <summary>Cancels a pending or running operation.</summary>
    public const string BulkCancel = "/api/bulks/{id:guid}/cancel";

    /// <summary>Completes a pipeline step waiting on an external signal.</summary>
    public const string BulkSignal = "/api/bulks/{id:guid}/signal/{key}";

    /// <summary>Fails a pipeline step waiting on an external signal.</summary>
    public const string BulkSignalFail = "/api/bulks/{id:guid}/signal/{key}/fail";

    /// <summary>Retries every failed row.</summary>
    public const string BulkRetry = "/api/bulks/{id:guid}/retry";

    /// <summary>Retries a specific set of rows.</summary>
    public const string BulkRetryRows = "/api/bulks/{id:guid}/retry/rows";

    /// <summary>Reports whether an operation can be retried.</summary>
    public const string BulkRetryEligibility = "/api/bulks/{id:guid}/retry/eligibility";

    /// <summary>Retry attempts recorded for an operation, paged.</summary>
    public const string BulkRetryHistory = "/api/bulks/{id:guid}/retry/history";

    /// <summary>Lifecycle events across all operations, for driving a UI notification feed.</summary>
    public const string Events = "/api/events";

    /// <summary>Lifecycle events for a single operation.</summary>
    public const string BulkEvents = "/api/bulks/{id:guid}/events";

    /// <summary>Every route in the contract. Used by conformance tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Operations,
        OperationTemplate,
        Bulks,
        BulksValidate,
        Bulk,
        BulkStatus,
        BulkErrors,
        BulkRows,
        BulkFile,
        BulkExport,
        BulkCancel,
        BulkSignal,
        BulkSignalFail,
        BulkRetry,
        BulkRetryRows,
        BulkRetryEligibility,
        BulkRetryHistory,
        Events,
        BulkEvents
    ];
}
