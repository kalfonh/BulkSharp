# Source-Based Gateway Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable the gateway to short-circuit list queries to a single backend when the caller specifies a `source` parameter, eliminating unnecessary fan-out.

**Architecture:** Make `BulkOperation.Source` non-nullable and always populated from `BulkSharpOptions.ServiceName`. Add `GatewayRouter.GetClientByServiceName()` to resolve backends by name. Modify `GatewayAggregator.AggregateListAsync` to skip fan-out when `source` is present in the query string.

**Tech Stack:** .NET 8, EF Core, System.Text.Json, xUnit, Moq, FluentAssertions

**Spec:** `docs/superpowers/specs/2026-03-19-source-based-gateway-routing-design.md`

---

### Task 1: Make `BulkOperation.Source` non-nullable

**Files:**
- Modify: `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs:19`
- Modify: `src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs:28`

- [ ] **Step 1: Change `Source` from `string?` to `string` with empty default**

In `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs`, line 19, change:
```csharp
public string? Source { get; set; }
```
to:
```csharp
public string Source { get; set; } = string.Empty;
```

- [ ] **Step 2: Add `.IsRequired()` to EF Core mapping**

In `src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs`, line 28, change:
```csharp
entity.Property(e => e.Source).HasMaxLength(200);
```
to:
```csharp
entity.Property(e => e.Source).HasMaxLength(200).IsRequired();
```

- [ ] **Step 3: Build and fix any nullability warnings**

Run: `dotnet build`
Expected: Clean build. If there are warnings about `Source` nullability in existing code (e.g., null checks in `GatewayRouter.cs:58-66`), update those sites to reflect the non-nullable type. The router's JSON parsing at `GatewayRouter.cs:58-66` reads `Source` from raw JSON — that code should remain as-is since it's parsing external JSON, not the domain model.

- [ ] **Step 4: Run all tests**

Run: `dotnet test --filter "Category!=E2E"`
Expected: All tests pass. No test should depend on `Source` being null.

- [ ] **Step 5: Commit**

```bash
git add src/BulkSharp.Core/Domain/Operations/BulkOperation.cs src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs
git commit -m "feat: make BulkOperation.Source non-nullable"
```

---

### Task 2: Auto-default `BulkSharpOptions.ServiceName`

**Files:**
- Modify: `src/BulkSharp/ServiceCollectionExtensions.cs:47` and `src/BulkSharp/ServiceCollectionExtensions.cs:114`
- Note: `src/BulkSharp.Core/Configuration/BulkSharpOptions.cs` — no code changes needed. The `ServiceName` property stays `string?` as-is. The defaulting is applied via `PostConfigure` in `ServiceCollectionExtensions.cs`, not on the property itself.
- Test: `tests/BulkSharp.UnitTests/Configuration/ServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write failing tests for ServiceName defaulting**

Add to `tests/BulkSharp.UnitTests/Configuration/ServiceCollectionExtensionsTests.cs`:

```csharp
[Fact]
public void AddBulkSharp_ServiceNameNull_DefaultsToNonEmpty()
{
    var services = new ServiceCollection();
    services.AddBulkSharp(builder => builder
        .UseFileStorage(fs => fs.UseInMemory())
        .UseMetadataStorage(ms => ms.UseInMemory())
        .UseScheduler(s => s.UseImmediate()));

    var sp = services.BuildServiceProvider();
    var options = sp.GetRequiredService<IOptions<BulkSharpOptions>>().Value;

    options.ServiceName.Should().NotBeNullOrEmpty();
}

[Fact]
public void AddBulkSharp_ServiceNameExplicit_PreservesValue()
{
    var services = new ServiceCollection();
    services.AddBulkSharp(builder => builder
        .ConfigureOptions(opts => opts.ServiceName = "my-service")
        .UseFileStorage(fs => fs.UseInMemory())
        .UseMetadataStorage(ms => ms.UseInMemory())
        .UseScheduler(s => s.UseImmediate()));

    var sp = services.BuildServiceProvider();
    var options = sp.GetRequiredService<IOptions<BulkSharpOptions>>().Value;

    options.ServiceName.Should().Be("my-service");
}

[Fact]
public void AddBulkSharpApi_ServiceNameNull_DefaultsToNonEmpty()
{
    var services = new ServiceCollection();
    services.AddBulkSharpApi(builder => builder
        .UseFileStorage(fs => fs.UseInMemory())
        .UseMetadataStorage(ms => ms.UseInMemory()));

    var sp = services.BuildServiceProvider();
    var options = sp.GetRequiredService<IOptions<BulkSharpOptions>>().Value;

    options.ServiceName.Should().NotBeNullOrEmpty();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BulkSharp.UnitTests/BulkSharp.UnitTests.csproj --filter "FullyQualifiedName~ServiceName"`
Expected: `ServiceNameNull_DefaultsToNonEmpty` tests FAIL (ServiceName is currently null by default).

- [ ] **Step 3: Implement ServiceName defaulting in `AddBulkSharp`**

In `src/BulkSharp/ServiceCollectionExtensions.cs`, add a `PostConfigure` **before** the existing validation `PostConfigure` at line 48. Insert before line 48:

```csharp
        // Default ServiceName if not explicitly configured
        services.AddOptions<BulkSharpOptions>()
            .PostConfigure(options =>
            {
                if (string.IsNullOrEmpty(options.ServiceName))
                    options.ServiceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "default";
            });
```

Important: This registers a separate `services.AddOptions<BulkSharpOptions>().PostConfigure(...)` call. Calling `AddOptions<T>()` multiple times is safe (idempotent). Because this registration appears before the existing validation `PostConfigure` at line 48, it executes first — so the default is applied before `Validate()` runs.

- [ ] **Step 4: Implement the same defaulting in `AddBulkSharpApi`**

In `src/BulkSharp/ServiceCollectionExtensions.cs`, add the same `PostConfigure` block before line 115 (the existing validation `PostConfigure` in `AddBulkSharpApi`):

```csharp
        // Default ServiceName if not explicitly configured
        services.AddOptions<BulkSharpOptions>()
            .PostConfigure(options =>
            {
                if (string.IsNullOrEmpty(options.ServiceName))
                    options.ServiceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "default";
            });
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/BulkSharp.UnitTests/BulkSharp.UnitTests.csproj --filter "FullyQualifiedName~ServiceName"`
Expected: All three new tests PASS.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test --filter "Category!=E2E"`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/BulkSharp/ServiceCollectionExtensions.cs tests/BulkSharp.UnitTests/Configuration/ServiceCollectionExtensionsTests.cs
git commit -m "feat: auto-default BulkSharpOptions.ServiceName"
```

---

### Task 3: Add `GatewayRouter.GetClientByServiceName()`

**Files:**
- Modify: `src/BulkSharp.Gateway/Routing/GatewayRouter.cs:94`
- Test: `tests/BulkSharp.Gateway.Tests/GatewayRouterTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `tests/BulkSharp.Gateway.Tests/GatewayRouterTests.cs`:

```csharp
[Fact]
public void GetClientByServiceName_RegisteredBackend_ReturnsClient()
{
    var clientA = CreateMockClient("service-a");
    var clientB = CreateMockClient("service-b");
    _clientFactory.Setup(f => f.GetAllClients())
        .Returns(new[] { clientA.Object, clientB.Object });

    var result = _sut.GetClientByServiceName("service-b");

    result.Should().NotBeNull();
    result!.ServiceName.Should().Be("service-b");
}

[Fact]
public void GetClientByServiceName_UnregisteredBackend_ReturnsNull()
{
    var clientA = CreateMockClient("service-a");
    _clientFactory.Setup(f => f.GetAllClients())
        .Returns(new[] { clientA.Object });

    var result = _sut.GetClientByServiceName("unknown");

    result.Should().BeNull();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BulkSharp.Gateway.Tests/BulkSharp.Gateway.Tests.csproj --filter "FullyQualifiedName~GetClientByServiceName"`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Implement `GetClientByServiceName`**

In `src/BulkSharp.Gateway/Routing/GatewayRouter.cs`, add after line 94 (after `GetAllClients()`):

```csharp
    public IBackendClient? GetClientByServiceName(string serviceName)
    {
        return _clientFactory.GetAllClients()
            .FirstOrDefault(c => string.Equals(c.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
    }
```

Note: Uses `GetAllClients()` (not `GetClient()`) because `GetClient()` always returns a client even for unregistered names. Case-insensitive match for robustness.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BulkSharp.Gateway.Tests/BulkSharp.Gateway.Tests.csproj --filter "FullyQualifiedName~GetClientByServiceName"`
Expected: Both tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BulkSharp.Gateway/Routing/GatewayRouter.cs tests/BulkSharp.Gateway.Tests/GatewayRouterTests.cs
git commit -m "feat: add GatewayRouter.GetClientByServiceName"
```

---

### Task 4: Short-circuit `AggregateListAsync` when `source` is present

**Files:**
- Modify: `src/BulkSharp.Gateway/Services/GatewayAggregator.cs:57-59`
- Test: `tests/BulkSharp.Gateway.Tests/GatewayAggregatorTests.cs`

- [ ] **Step 1: Write failing tests for source-based short-circuit**

Add to `tests/BulkSharp.Gateway.Tests/GatewayAggregatorTests.cs`:

```csharp
[Fact]
public async Task AggregateListAsync_SourceMatchesBackend_RoutesToSingleBackend()
{
    var clientA = CreateMockClient("service-a");
    var clientB = CreateMockClient("service-b");

    var listA = JsonSerializer.Serialize(new
    {
        items = new[] { new { id = Guid.NewGuid(), createdAt = "2026-03-17T10:00:00Z" } },
        totalCount = 1,
        page = 1,
        pageSize = 20
    });
    clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(listA)
        });

    _clientFactory.Setup(f => f.GetAllClients())
        .Returns(new[] { clientA.Object, clientB.Object });

    var result = await _sut.AggregateListAsync("?source=service-a&page=1&pageSize=20", CancellationToken.None);

    // service-b should NOT have been called
    clientB.Verify(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    var json = JsonSerializer.Serialize(result);
    json.Should().Contain("totalCount");
}

[Fact]
public async Task AggregateListAsync_SourceNoMatch_FallsBackToFanOut()
{
    var clientA = CreateMockClient("service-a");

    var listA = JsonSerializer.Serialize(new
    {
        items = new[] { new { id = Guid.NewGuid(), createdAt = "2026-03-17T10:00:00Z" } },
        totalCount = 1
    });
    clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(listA)
        });

    _clientFactory.Setup(f => f.GetAllClients())
        .Returns(new[] { clientA.Object });

    // "unknown-service" doesn't match any backend — should fan out
    var result = await _sut.AggregateListAsync("?source=unknown-service&page=1&pageSize=20", CancellationToken.None);

    // Fan-out happened, so service-a WAS called
    clientA.Verify(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task AggregateListAsync_SourceStrippedFromForwardedQueryString()
{
    var clientA = CreateMockClient("service-a");

    var listA = JsonSerializer.Serialize(new
    {
        items = Array.Empty<object>(),
        totalCount = 0,
        page = 1,
        pageSize = 10
    });
    clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(listA)
        });

    _clientFactory.Setup(f => f.GetAllClients())
        .Returns(new[] { clientA.Object });

    await _sut.AggregateListAsync("?source=service-a&page=2&pageSize=10", CancellationToken.None);

    // Verify source was stripped but other params preserved
    clientA.Verify(c => c.GetBulksAsync(
        It.Is<string>(qs => qs.Contains("page=2") && qs.Contains("pageSize=10") && !qs.Contains("source")),
        It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task AggregateListAsync_SourceOnlyParam_ForwardsEmptyQueryString()
{
    var clientA = CreateMockClient("service-a");

    var listA = JsonSerializer.Serialize(new
    {
        items = Array.Empty<object>(),
        totalCount = 0,
        page = 1,
        pageSize = 20
    });
    clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(listA)
        });

    _clientFactory.Setup(f => f.GetAllClients())
        .Returns(new[] { clientA.Object });

    await _sut.AggregateListAsync("?source=service-a", CancellationToken.None);

    // Should forward empty string, not "?"
    clientA.Verify(c => c.GetBulksAsync(
        It.Is<string>(qs => qs == ""),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BulkSharp.Gateway.Tests/BulkSharp.Gateway.Tests.csproj --filter "FullyQualifiedName~Source"`
Expected: Tests FAIL — current implementation always fans out.

- [ ] **Step 3: Implement short-circuit logic in `AggregateListAsync`**

In `src/BulkSharp.Gateway/Services/GatewayAggregator.cs`:

1. Add `using System.Web;` to the top of the file (after existing usings, around line 4).

2. Insert the following block **after** line 58 (the opening `{` of the method) and **before** line 59 (`var clients = ...`). The existing line 59 and everything after it stays exactly as-is — the new block is an early-return guard that sits above the existing fan-out logic:

```csharp
        // Check for source-based short-circuit
        var parsedQs = HttpUtility.ParseQueryString(queryString);
        var source = parsedQs["source"];
        if (!string.IsNullOrEmpty(source))
        {
            var targetClient = _router.GetClientByServiceName(source);
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
                    return JsonSerializer.Deserialize<object>(json, JsonOptions)!;
                }

                return new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 20, HasNextPage = false };
            }

            // source didn't match any backend — log warning and fall through to fan-out
            _logger.LogWarning("Source parameter '{Source}' does not match any registered backend. Falling back to fan-out.", source);
        }

        // Existing fan-out code continues unchanged from here (line 59: var clients = ...)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BulkSharp.Gateway.Tests/BulkSharp.Gateway.Tests.csproj`
Expected: All tests PASS (existing + new).

- [ ] **Step 5: Run full test suite**

Run: `dotnet test --filter "Category!=E2E"`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/BulkSharp.Gateway/Services/GatewayAggregator.cs tests/BulkSharp.Gateway.Tests/GatewayAggregatorTests.cs
git commit -m "feat: short-circuit AggregateListAsync when source param is present"
```

---

### Task 5: Integration test for source-based routing

**Files:**
- Modify: `tests/BulkSharp.Gateway.IntegrationTests/GatewayEndpointTests.cs`

- [ ] **Step 1: Write integration test**

Add to `tests/BulkSharp.Gateway.IntegrationTests/GatewayEndpointTests.cs`:

```csharp
[Fact]
public async Task AggregatedList_WithSource_RoutesToSingleBackend()
{
    var id1 = Guid.NewGuid();

    var backend1 = new FakeBackendHandler("svc-a")
        .OnGet("api/operations", new[] { new { Name = "op-a" } })
        .OnGet("api/bulks", new
        {
            Items = new[] { new { Id = id1.ToString(), OperationName = "op-a", CreatedAt = "2026-03-17T10:00:00Z", Source = "svc-a", Status = 0 } },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        });

    // backend2 has data but should NOT be contacted
    var backend2 = new FakeBackendHandler("svc-b")
        .OnGet("api/operations", new[] { new { Name = "op-b" } })
        .OnGet("api/bulks", new
        {
            Items = new[] { new { Id = Guid.NewGuid().ToString(), OperationName = "op-b", CreatedAt = "2026-03-17T11:00:00Z", Source = "svc-b", Status = 0 } },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        });

    using var client = _fixture.CreateGatewayClient(("svc-a", backend1), ("svc-b", backend2));

    var response = await client.GetAsync("/api/bulks?source=svc-a&page=1&pageSize=20");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await response.Content.ReadAsStringAsync();
    var doc = JsonDocument.Parse(json);

    // Should only contain svc-a's operation
    var totalCount = doc.RootElement.GetProperty("totalCount").GetInt32();
    totalCount.Should().Be(1);

    var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
    items.Should().HaveCount(1);
    items[0].GetProperty("operationName").GetString().Should().Be("op-a");
}
```

- [ ] **Step 2: Run the integration test**

Run: `dotnet test tests/BulkSharp.Gateway.IntegrationTests/BulkSharp.Gateway.IntegrationTests.csproj --filter "FullyQualifiedName~WithSource"`
Expected: PASS.

- [ ] **Step 3: Run full integration suite to verify no regressions**

Run: `dotnet test tests/BulkSharp.Gateway.IntegrationTests/BulkSharp.Gateway.IntegrationTests.csproj`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/BulkSharp.Gateway.IntegrationTests/GatewayEndpointTests.cs
git commit -m "test: add integration test for source-based gateway routing"
```

---

### Task 6: Update sample gateway to demonstrate source routing

**Files:**
- Modify: `samples/BulkSharp.Sample.Gateway/Program.cs`

- [ ] **Step 1: Update sample to show ServiceName alignment**

In `samples/BulkSharp.Sample.Gateway/Program.cs`, the current `AddBackend("webapp")` call doesn't demonstrate the `source` parameter or multi-backend setup. Update it to show the recommended pattern. Replace lines 5-11:

```csharp
// Register the gateway — backend names must match each backend's BulkSharpOptions.ServiceName
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("webapp")
    .ConfigureResilience(opts =>
    {
        opts.HttpTimeout = TimeSpan.FromSeconds(30);
        opts.RegistryRefreshInterval = TimeSpan.FromMinutes(1);
    }));
```

Add a comment documenting the `source` query parameter:

```csharp
// Register the gateway
// Backend names here must match each backend's BulkSharpOptions.ServiceName for source-based routing.
// Clients can use GET /api/bulks?source=webapp to route directly to this backend (skips fan-out).
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("webapp")
    .ConfigureResilience(opts =>
    {
        opts.HttpTimeout = TimeSpan.FromSeconds(30);
        opts.RegistryRefreshInterval = TimeSpan.FromMinutes(1);
    }));
```

- [ ] **Step 2: Verify sample builds**

Run: `dotnet build samples/BulkSharp.Sample.Gateway/BulkSharp.Sample.Gateway.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add samples/BulkSharp.Sample.Gateway/Program.cs
git commit -m "docs: document source-based routing in gateway sample"
```

---

### Task 7: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test --filter "Category!=E2E"`
Expected: All tests pass.

- [ ] **Step 3: Verify no untracked files**

Run: `git status`
Expected: Clean working tree.
