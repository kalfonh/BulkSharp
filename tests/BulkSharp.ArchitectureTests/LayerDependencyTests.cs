using NetArchTest.Rules;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Scheduling;

namespace BulkSharp.ArchitectureTests;

/// <summary>
/// Enforces dependency direction: Infrastructure -> Processing -> Core.
/// Core must never reference Processing, EF, or Dashboard.
/// </summary>
[Trait("Category", "Architecture")]
public class LayerDependencyTests
{
    private static readonly System.Reflection.Assembly CoreAssembly = typeof(BulkOperation).Assembly;
    private static readonly System.Reflection.Assembly ProcessingAssembly = typeof(ChannelsSchedulerOptions).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(BulkSharp.Api.WebApplicationExtensions).Assembly;

    [Fact]
    public void Core_ShouldNotReference_Processing()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Processing")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core references Processing: {FormatFailing(result)}");
    }

    [Fact]
    public void Core_ShouldNotReference_EntityFramework()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core references EF Core: {FormatFailing(result)}");
    }

    [Fact]
    public void Core_ShouldNotReference_StorageEntityFramework()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Data.EntityFramework")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core references Data.EntityFramework: {FormatFailing(result)}");
    }

    [Fact]
    public void Core_ShouldNotReference_Dashboard()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Dashboard")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core references Dashboard: {FormatFailing(result)}");
    }

    [Fact]
    public void Core_ShouldNotReference_Api()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core references Api: {FormatFailing(result)}");
    }

    /// <summary>
    /// The whole point of extracting BulkSharp.Api is that a host can expose the HTTP API
    /// without taking a Razor or Blazor dependency. A single stray using in this assembly
    /// silently reintroduces it, and nobody notices until an adopter complains about
    /// package size. This test is the guard.
    /// </summary>
    [Fact]
    public void Api_ShouldNotReference_Dashboard()
    {
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Dashboard")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Api references Dashboard, defeating the headless split: {FormatFailing(result)}");
    }

    [Fact]
    public void Api_ShouldNotReference_Blazor()
    {
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.Components")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Api references Blazor components: {FormatFailing(result)}");
    }

    [Fact]
    public void Core_ShouldNotReference_CsvHelper()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("CsvHelper")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core references CsvHelper: {FormatFailing(result)}");
    }

    [Fact]
    public void Processing_ShouldNotReference_EntityFrameworkStorage()
    {
        var result = Types.InAssembly(ProcessingAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Data.EntityFramework")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Processing references Data.EntityFramework: {FormatFailing(result)}");
    }

    [Fact]
    public void Processing_ShouldNotReference_Dashboard()
    {
        var result = Types.InAssembly(ProcessingAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Dashboard")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Processing references Dashboard: {FormatFailing(result)}");
    }

    [Fact]
    public void Gateway_ShouldNotReference_Processing()
    {
        var gatewayAssembly = typeof(BulkSharp.Gateway.Registry.OperationRegistryEntry).Assembly;
        var result = Types.InAssembly(gatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Processing")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Gateway references Processing: {FormatFailing(result)}");
    }

    [Fact]
    public void Gateway_ShouldNotReference_Dashboard()
    {
        var gatewayAssembly = typeof(BulkSharp.Gateway.Registry.OperationRegistryEntry).Assembly;
        var result = Types.InAssembly(gatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("BulkSharp.Dashboard")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Gateway references Dashboard: {FormatFailing(result)}");
    }

    private static string FormatFailing(TestResult result) =>
        string.Join(", ", result.FailingTypeNames ?? []);
}
