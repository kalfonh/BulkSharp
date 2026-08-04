using System.Net.Http.Json;
using BulkSharp.Core.Contracts;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Dashboard.Services;

/// <summary>
/// Typed client for the BulkSharp HTTP API.
/// </summary>
/// <remarks>
/// The dashboard consumes the same contract an external front end does. Routing every
/// page through this client keeps URL construction and serialization in one place, and
/// means the built-in UI exercises the public API rather than reaching around it — if a
/// page can't be built on these endpoints, neither can anyone else's.
/// </remarks>
/// <param name="http">The HTTP client, configured with the application's base address.</param>
public sealed class BulkSharpApiClient(HttpClient http)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = BulkSharpJsonSerialization.Options;

    /// <summary>Queries bulk operations with filtering and paging.</summary>
    /// <param name="query">The query to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task<PagedResult<BulkOperation>> QueryBulksAsync(
        BulkOperationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>
        {
            $"page={query.Page}",
            $"pageSize={query.PageSize}"
        };

        if (!string.IsNullOrWhiteSpace(query.OperationName))
            parameters.Add($"operationName={Uri.EscapeDataString(query.OperationName)}");
        if (!string.IsNullOrWhiteSpace(query.CreatedBy))
            parameters.Add($"createdBy={Uri.EscapeDataString(query.CreatedBy)}");
        if (query.Status is { } status)
            parameters.Add($"status={status}");
        if (query.FromDate is { } from)
            parameters.Add($"fromDate={Uri.EscapeDataString(from.ToString("O"))}");
        if (query.ToDate is { } to)
            parameters.Add($"toDate={Uri.EscapeDataString(to.ToString("O"))}");
        if (!string.IsNullOrWhiteSpace(query.SortBy))
            parameters.Add($"sortBy={Uri.EscapeDataString(query.SortBy)}");

        var url = $"api/bulks?{string.Join("&", parameters)}";

        return await http.GetFromJsonAsync<PagedResult<BulkOperation>>(url, Json, cancellationToken)
               ?? EmptyPage(query);
    }

    /// <summary>Returns a single operation, or null when it does not exist.</summary>
    /// <param name="operationId">The operation identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task<BulkOperation?> GetBulkAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/bulks/{operationId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<BulkOperation>(Json, cancellationToken);
    }

    /// <summary>Returns a progress snapshot for polling.</summary>
    /// <param name="operationId">The operation identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task<BulkStatusDto?> GetStatusAsync(Guid operationId, CancellationToken cancellationToken = default)
        => http.GetFromJsonAsync<BulkStatusDto>($"api/bulks/{operationId}/status", Json, cancellationToken);

    /// <summary>Cancels a pending or running operation.</summary>
    /// <param name="operationId">The operation identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the cancellation was accepted.</returns>
    public async Task<bool> CancelAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsync($"api/bulks/{operationId}/cancel", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Lists the operations available for submission.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task<IReadOnlyList<OperationDescriptorDto>> GetOperationsAsync(
        CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<List<OperationDescriptorDto>>("api/operations", Json, cancellationToken)
           ?? [];

    private static PagedResult<BulkOperation> EmptyPage(BulkOperationQuery query) => new()
    {
        Items = [],
        TotalCount = 0,
        Page = query.Page,
        PageSize = query.PageSize
    };
}
