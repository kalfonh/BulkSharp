using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Routing;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BulkSharp.Gateway.Services;

public sealed class GatewayAggregator
{
    private readonly GatewayRouter _router;
    private readonly ILogger<GatewayAggregator> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GatewayAggregator(GatewayRouter router, ILogger<GatewayAggregator> logger)
    {
        _router = router;
        _logger = logger;
    }

    public async Task<List<JsonElement>> AggregateDiscoveryAsync(CancellationToken ct)
    {
        var clients = _router.GetAllClients().ToList();
        var allOperations = new List<JsonElement>();

        var tasks = clients.Select(async client =>
        {
            try
            {
                using var response = await client.GetOperationsAsync(ct);
                if (!response.IsSuccessStatusCode) return new List<JsonElement>();

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
                _logger.AggregateDiscoveryFailed(ex, client.ServiceName);
                return new List<JsonElement>();
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var batch in results)
            allOperations.AddRange(batch);

        return allOperations;
    }

    public async Task<object> AggregateListAsync(string queryString, CancellationToken ct)
    {
        var clients = _router.GetAllClients().ToList();

        var tasks = clients.Select(async client =>
        {
            try
            {
                using var response = await client.GetBulksAsync(queryString, ct);
                if (!response.IsSuccessStatusCode) return (Items: new List<JsonElement>(), Total: 0);

                var json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var items = new List<JsonElement>();
                if (root.TryGetProperty("items", out var itemsProp) || root.TryGetProperty("Items", out itemsProp))
                {
                    foreach (var item in itemsProp.EnumerateArray())
                    {
                        items.Add(item);

                        // Cache Source for routing
                        if (item.TryGetProperty("id", out var idProp) || item.TryGetProperty("Id", out idProp))
                        {
                            if (Guid.TryParse(idProp.GetString(), out var opId))
                                _router.CacheSource(opId, client.ServiceName);
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
                _logger.AggregateListFailed(ex, client.ServiceName);
                return (Items: new List<JsonElement>(), Total: 0);
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

        // Parse requested page/pageSize from query string for re-pagination
        var qs = System.Web.HttpUtility.ParseQueryString(queryString);
        var page = int.TryParse(qs["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(qs["pageSize"], out var ps) ? ps : 20;

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
