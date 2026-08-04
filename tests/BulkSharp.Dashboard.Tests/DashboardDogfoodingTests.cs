using System.Reflection;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BulkSharp.Dashboard.Tests;

/// <summary>
/// The dashboard must consume the public HTTP API, not the in-process services behind it.
/// </summary>
/// <remarks>
/// A component that reaches around the API leaves that capability unproven: an external
/// front end building the same screen has only the HTTP contract to work with, and would
/// be the first to discover a gap. <c>OperationList</c> previously injected
/// <c>IBulkOperationService</c> directly and so exercised nothing.
/// </remarks>
public class DashboardDogfoodingTests
{
    /// <summary>Service types that only exist in-process and must never be injected into a component.</summary>
    private static readonly Type[] ServerSideOnlyServices =
    [
        typeof(IBulkOperationService),
        typeof(IBulkOperationDiscovery)
    ];

    public static TheoryData<Type> DashboardComponents()
    {
        var data = new TheoryData<Type>();

        var components = typeof(BulkSharpApiClient).Assembly
            .GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var component in components)
            data.Add(component);

        return data;
    }

    [Theory]
    [MemberData(nameof(DashboardComponents))]
    public void Component_DoesNotInjectServerSideOnlyServices(Type component)
    {
        var injected = component
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
            .Select(p => p.PropertyType)
            .ToList();

        var offending = injected.Where(t => ServerSideOnlyServices.Contains(t)).ToList();

        Assert.True(
            offending.Count == 0,
            $"{component.Name} injects {string.Join(", ", offending.Select(t => t.Name))} instead of " +
            $"consuming the HTTP API. Add what it needs to {nameof(BulkSharpApiClient)}.");
    }

    /// <summary>
    /// Guards against the theory silently covering nothing if component discovery breaks.
    /// </summary>
    [Fact]
    public void ComponentDiscovery_FindsTheDashboardComponents()
    {
        var components = DashboardComponents().Select(row => (Type)row[0]!).ToList();

        Assert.True(components.Count >= 5, $"Only discovered {components.Count} components.");
    }
}
