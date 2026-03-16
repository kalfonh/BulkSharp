using System.Reflection;
using NetArchTest.Rules;
using BulkSharp.Core.Configuration;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Scheduling;

namespace BulkSharp.ArchitectureTests;

/// <summary>
/// Enforces coding conventions as executable tests.
/// </summary>
[Trait("Category", "Architecture")]
public class ConventionTests
{
    private static readonly Assembly CoreAssembly = typeof(BulkOperation).Assembly;
    private static readonly Assembly ProcessingAssembly = typeof(ChannelsSchedulerOptions).Assembly;
    private static readonly Assembly EfAssembly = typeof(BulkSharp.Data.EntityFramework.BulkSharpDbContext).Assembly;

    [Fact]
    public void ProcessingClasses_ShouldBeSealed()
    {
        var result = Types.InAssembly(ProcessingAssembly)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .DoNotHaveNameEndingWith("Extensions")
            .Should()
            .BeSealed()
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Unsealed processing classes: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void EfRepositories_ShouldBeSealed()
    {
        var result = Types.InAssembly(EfAssembly)
            .That()
            .HaveNameEndingWith("Repository")
            .Should()
            .BeSealed()
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Unsealed EF repositories: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OptionsClasses_ShouldHaveParameterlessConstructor()
    {
        var optionTypes = new[]
        {
            typeof(BulkSharpOptions),
            typeof(ChannelsSchedulerOptions)
        };

        foreach (var type in optionTypes)
        {
            Assert.True(
                type.GetConstructor(Type.EmptyTypes) != null,
                $"{type.Name} must have a parameterless constructor for IConfiguration binding");
        }
    }

    [Fact]
    public void DomainModels_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That()
            .ResideInNamespace("BulkSharp.Core.Domain")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain types reference EF Core: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Exceptions_ShouldInheritFromBulkProcessingException()
    {
        var exceptionTypes = CoreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Exception")
                        && t.Namespace?.Contains("Exceptions") == true
                        && t != typeof(BulkSharp.Core.Exceptions.BulkProcessingException));

        foreach (var type in exceptionTypes)
        {
            Assert.True(
                typeof(BulkSharp.Core.Exceptions.BulkProcessingException).IsAssignableFrom(type),
                $"{type.Name} should inherit from BulkProcessingException");
        }
    }

    [Fact]
    public void LeafExceptions_ShouldBeSealed()
    {
        var baseType = typeof(BulkSharp.Core.Exceptions.BulkProcessingException);
        var allExceptionTypes = baseType.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t) && t != baseType)
            .ToList();

        // Leaf exceptions = those that are not base types of any other exception in the hierarchy
        var leafExceptions = allExceptionTypes
            .Where(t => !allExceptionTypes.Any(sub => sub.BaseType == t))
            .ToList();

        foreach (var type in leafExceptions)
        {
            Assert.True(type.IsSealed,
                $"{type.Name} is a leaf exception and should be sealed");
        }
    }

    [Fact]
    public void Interfaces_InCore_ShouldBePublic()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That()
            .AreInterfaces()
            .And()
            .ResideInNamespace("BulkSharp.Core.Abstractions")
            .Should()
            .BePublic()
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Non-public abstractions: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [InlineData("IBulkRetryService", "BulkSharp.Core.Abstractions.Operations")]
    [InlineData("IBulkExportService", "BulkSharp.Core.Abstractions.Operations")]
    [InlineData("IBulkExportFormatter", "BulkSharp.Core.Abstractions.Export")]
    [InlineData("IBulkRowRetryHistoryRepository", "BulkSharp.Core.Abstractions.Storage")]
    public void RetryAndExportInterfaces_ShouldBeDefinedInCore(string typeName, string expectedNamespace)
    {
        var type = CoreAssembly.GetTypes().SingleOrDefault(t => t.Name == typeName);
        Assert.NotNull(type);
        Assert.Equal(expectedNamespace, type.Namespace);
    }

    [Theory]
    [InlineData("BulkRetryService", "BulkSharp.Processing.Services")]
    [InlineData("BulkExportService", "BulkSharp.Processing.Services")]
    [InlineData("DefaultBulkExportFormatter", "BulkSharp.Processing.Export")]
    public void RetryAndExportImplementations_ShouldBeDefinedInProcessing(string typeName, string expectedNamespace)
    {
        var type = ProcessingAssembly.GetTypes().SingleOrDefault(t => t.Name == typeName);
        Assert.NotNull(type);
        Assert.Equal(expectedNamespace, type.Namespace);
    }
}
