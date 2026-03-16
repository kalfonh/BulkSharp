using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Routing;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Web;

namespace BulkSharp.Gateway.Services;

public sealed class GatewayAggregator(GatewayRouter router, ILogger<GatewayAggregator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<JsonElement>> AggregateDiscoveryAsync(CancellationToken ct)
    {
        var clients = router.GetAllClients().ToList();
        List<JsonElement> allOperations = [];

        var tasks = clients.Select(async client =>
        {
            try
            {
                using var response = await client.GetOperationsAsync(ct);
                if (!response.IsSuccessStatusCode) return [];

                var json = await response.Content.ReadAsStringAsync(ct);
                var ops = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions) ?? new();

                // Tag each with sourceService
                return ops.Select(op =>
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(op.GetRawText(), JsonOptions) ?? new();
                    dict["sourceService"] = client.ServiceName;
                    return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(dict, JsonOptions));
                }).ToList();
            }
            catch (Exception ex)
            {
                logger.AggregateDiscoveryFailed(ex, client.ServiceName);
                return [];
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var batch in results)
            allOperations.AddRange(batch);

        return allOperations;
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

        var tasks = clients.Select(async client =>
        {
            try
            {
                using var response = await client.GetBulksAsync(queryString, ct);
                if (!response.IsSuccessStatusCode) return (Items: [], Total: 0);

                var json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                List<JsonElement> items = [];
                if (root.TryGetProperty("items", out var itemsProp) || root.TryGetProperty("Items", out itemsProp))
                {
                    foreach (var item in itemsProp.EnumerateArray())
                    {
                        items.Add(item);

                        // Cache Source for routing
                        if (item.TryGetProperty("id", out var idProp) || item.TryGetProperty("Id", out idProp))
                        {
                            if (Guid.TryParse(idProp.GetString(), out var opId))
                                router.CacheSource(opId, client.ServiceName);
                        }
                    }
                }

                var total = 0;
                if (root.TryGetProperty("totalCount", out var totalProp) || root.TryGetProperty("TotalCount", out totalProp))
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
            var aDate = GetDateProperty(a, "createdAt") ?? GetDateProperty(a, "CreatedAt");
            var bDate = GetDateProperty(b, "createdAt") ?? GetDateProperty(b, "CreatedAt");
            return (bDate ?? DateTime.MinValue).CompareTo(aDate ?? DateTime.MinValue);
        });

        // Re-use already-parsed query string for re-pagination
        var page = int.TryParse(parsedQs["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(parsedQs["pageSize"], out var ps) ? ps : 20;

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

    private static DateTime? GetDateProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(prop.GetString(), out var dt)) return dt;
        }
        return null;
    }
}
