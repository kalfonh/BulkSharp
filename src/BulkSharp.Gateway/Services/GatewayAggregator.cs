using System.Globalization;
using BulkSharp.Core.Contracts;
using BulkSharp.Gateway.Configuration;
using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Routing;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Web;

namespace BulkSharp.Gateway.Services;

public sealed class GatewayAggregator(
    GatewayRouter router,
    BulkSharpGatewayOptions options,
    ILogger<GatewayAggregator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = BulkSharpJsonSerialization.Options;

    /// <summary>
    /// Upper bound on how many rows a single backend is asked for during fan-out.
    /// Deep paging beyond this is not supported without a <c>source</c> filter, and is
    /// logged rather than silently truncated — a truncated page reads to a client as
    /// "there is no more data".
    /// </summary>
    private const int MaxOverFetch = 1000;

    public async Task<List<OperationDescriptorDto>> AggregateDiscoveryAsync(CancellationToken ct)
    {
        var clients = router.GetAllClients().ToList();

        var tasks = clients.Select(async client =>
        {
            try
            {
                using var perBackend = CreatePerBackendToken(ct);
                using var response = await client.GetOperationsAsync(perBackend.Token);
                if (!response.IsSuccessStatusCode) return new List<OperationDescriptorDto>();

                var json = await response.Content.ReadAsStringAsync(perBackend.Token);
                var ops = JsonSerializer.Deserialize<List<OperationDescriptorDto>>(json, JsonOptions) ?? [];

                // Tag each operation with the backend that owns it so clients can route.
                return ops.Select(op => op with { SourceService = client.ServiceName }).ToList();
            }
            catch (Exception ex)
            {
                logger.AggregateDiscoveryFailed(ex, client.ServiceName);
                return new List<OperationDescriptorDto>();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(batch => batch).ToList();
    }

    public async Task<object> AggregateListAsync(string queryString, CancellationToken ct)
    {
        // Check for source-based short-circuit
        var parsedQs = HttpUtility.ParseQueryString(queryString);
        var source = parsedQs["source"];
        if (!string.IsNullOrEmpty(source))
        {
            var targetClient = router.GetClientByServiceName(source);
            if (targetClient != null)
            {
                // Strip source param and reconstruct query string
                parsedQs.Remove("source");
                var strippedQs = parsedQs.ToString();
                var forwardedQs = string.IsNullOrEmpty(strippedQs) ? "" : $"?{strippedQs}";

                using var response = await targetClient.GetBulksAsync(forwardedQs, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<object>(json, JsonOptions)
                        ?? new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 20, HasNextPage = false };
                }

                return new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 20, HasNextPage = false };
            }

            // source didn't match any backend — log warning and fall through to fan-out
            logger.SourceBackendNotFound(source);

            // Strip source from query string before fan-out (backends don't understand it)
            parsedQs.Remove("source");
            var stripped = parsedQs.ToString();
            queryString = string.IsNullOrEmpty(stripped) ? "" : $"?{stripped}";
        }

        var clients = router.GetAllClients().ToList();

        var page = int.TryParse(parsedQs["page"], out var p) && p > 0 ? p : 1;
        var pageSize = int.TryParse(parsedQs["pageSize"], out var ps) && ps > 0 ? ps : 20;

        // Each backend must return a correct prefix of its own ordering, deep enough that
        // the merged prefix covers the requested page. Forwarding the caller's page/pageSize
        // would only ever surface pageSize x backendCount rows, so page 2 of a multi-backend
        // merge would not be the true second page of the merged ordering.
        var overFetch = Math.Min(page * pageSize, MaxOverFetch);

        if (page * pageSize > MaxOverFetch)
            logger.FanOutPagingTruncated(page, pageSize, MaxOverFetch);

        var backendQs = HttpUtility.ParseQueryString(queryString);
        backendQs["page"] = "1";
        backendQs["pageSize"] = overFetch.ToString(CultureInfo.InvariantCulture);
        var fanOutQueryString = $"?{backendQs}";

        var tasks = clients.Select(async client =>
        {
            try
            {
                using var perBackend = CreatePerBackendToken(ct);
                using var response = await client.GetBulksAsync(fanOutQueryString, perBackend.Token);
                if (!response.IsSuccessStatusCode) return (Items: [], Total: 0);

                var json = await response.Content.ReadAsStringAsync(perBackend.Token);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                List<JsonElement> items = [];
                if (root.TryGetProperty("items", out var itemsProp))
                {
                    foreach (var item in itemsProp.EnumerateArray())
                    {
                        items.Add(item);

                        // Cache Source for routing
                        if (item.TryGetProperty("id", out var idProp)
                            && Guid.TryParse(idProp.GetString(), out var opId))
                        {
                            router.CacheSource(opId, client.ServiceName);
                        }
                    }
                }

                var total = 0;
                if (root.TryGetProperty("totalCount", out var totalProp))
                    total = totalProp.GetInt32();

                return (Items: items, Total: total);
            }
            catch (Exception ex)
            {
                logger.AggregateListFailed(ex, client.ServiceName);
                return (Items: [], Total: 0);
            }
        });

        var results = await Task.WhenAll(tasks);

        var allItems = results.SelectMany(r => r.Items).ToList();
        var totalCount = results.Sum(r => r.Total);

        // Re-sort by CreatedAt descending (approximate merge)
        allItems.Sort((a, b) =>
        {
            var aDate = GetDateProperty(a, "createdAt");
            var bDate = GetDateProperty(b, "createdAt");
            return (bDate ?? DateTime.MinValue).CompareTo(aDate ?? DateTime.MinValue);
        });

        var paged = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new
        {
            Items = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = page * pageSize < totalCount
        };
    }

    /// <summary>
    /// Bounds a single backend's contribution to a fan-out. Without this one slow backend
    /// holds the whole aggregation open for the full HTTP timeout. A backend that exceeds
    /// the bound is caught by the surrounding handler and degrades to an empty result,
    /// which is the intended behaviour.
    /// </summary>
    private CancellationTokenSource CreatePerBackendToken(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(options.FanOutTimeoutPerBackend);
        return source;
    }

    private static DateTime? GetDateProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(prop.GetString(), out var dt)) return dt;
        }
        return null;
    }
}
