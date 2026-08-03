using System.Net.Http.Json;
using System.Security.Claims;
using BulkSharp.Api;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BulkSharp.Api.Tests;

[Trait("Category", "Integration")]
public class CreatedByTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>
    /// Set per-test to control the principal the request arrives with.
    /// Null means the request is anonymous.
    /// </summary>
    private ClaimsPrincipal? _principal;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddBulkSharp(b =>
        {
            b.UseFileStorage(fs => fs.UseInMemory())
             .UseMetadataStorage(ms => ms.UseInMemory())
             .UseScheduler(s => s.UseImmediate());
        });
        builder.Services.AddBulkSharpEndpoints();

        _app = builder.Build();

        // Stands in for real authentication middleware.
        _app.Use(async (context, next) =>
        {
            if (_principal is not null)
                context.User = _principal;
            await next();
        });

        _app.MapBulkSharpEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    private async Task<Guid> CreateAsync(string clientSuppliedCreatedBy)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("probe-operation"), "operationName" },
            { new StringContent(clientSuppliedCreatedBy), "createdBy" },
            { new StringContent("{\"AccountId\":\"acct-1\"}"), "metadata" }
        };
        content.Add(new StringContent("Email Address,DisplayName\nuser@example.com,User\n"), "file", "rows.csv");

        var response = await _client.PostAsync("/api/bulks", content);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateOperationResponse>(
            BulkSharpJsonSerialization.Options);

        return created!.OperationId;
    }

    private async Task<string> ReadCreatedByAsync(Guid operationId)
    {
        using var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = await repo.GetByIdAsync(operationId, CancellationToken.None);
        return operation!.CreatedBy;
    }

    [Fact]
    public async Task CreateBulk_WhenAuthenticated_IgnoresClientSuppliedCreatedBy()
    {
        _principal = Authenticated(new Claim(ClaimTypes.NameIdentifier, "real.user"));

        var id = await CreateAsync(clientSuppliedCreatedBy: "attacker");

        Assert.Equal("real.user", await ReadCreatedByAsync(id));
    }

    [Fact]
    public async Task CreateBulk_PrefersSubjectIdentifierOverOtherClaims()
    {
        _principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "sub-123"),
            new Claim("preferred_username", "display.name"),
            new Claim(ClaimTypes.Name, "identity.name"));

        var id = await CreateAsync(clientSuppliedCreatedBy: "attacker");

        Assert.Equal("sub-123", await ReadCreatedByAsync(id));
    }

    [Fact]
    public async Task CreateBulk_FallsBackToPreferredUsername()
    {
        _principal = Authenticated(new Claim("preferred_username", "display.name"));

        var id = await CreateAsync(clientSuppliedCreatedBy: "attacker");

        Assert.Equal("display.name", await ReadCreatedByAsync(id));
    }

    /// <summary>
    /// Unauthenticated self-hosting is a supported BulkSharp deployment mode, so the
    /// form value remains the fallback rather than being rejected outright.
    /// </summary>
    [Fact]
    public async Task CreateBulk_WhenAnonymous_AcceptsTheFormValue()
    {
        _principal = null;

        var id = await CreateAsync(clientSuppliedCreatedBy: "self-hosted-caller");

        Assert.Equal("self-hosted-caller", await ReadCreatedByAsync(id));
    }

    [Fact]
    public async Task CreateBulk_WhenPrincipalHasNoUsableClaim_FallsBackToTheFormValue()
    {
        _principal = Authenticated(new Claim("some_unrelated_claim", "x"));

        var id = await CreateAsync(clientSuppliedCreatedBy: "fallback-caller");

        Assert.Equal("fallback-caller", await ReadCreatedByAsync(id));
    }
}
