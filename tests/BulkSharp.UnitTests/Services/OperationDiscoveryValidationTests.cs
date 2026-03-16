using BulkSharp.Core.Domain.Discovery;
using BulkSharp.Processing.Services;

namespace BulkSharp.UnitTests.Services;

[Trait("Category", "Unit")]
public class OperationDiscoveryValidationTests
{
    [Fact]
    public void ValidateUniqueNames_WithUniqueNames_DoesNotThrow()
    {
        var operations = new[]
        {
            new BulkOperationInfo { Name = "operation-a", OperationType = typeof(string) },
            new BulkOperationInfo { Name = "operation-b", OperationType = typeof(int) },
            new BulkOperationInfo { Name = "operation-c", OperationType = typeof(bool) }
        };

        var act = () => BulkOperationDiscoveryService.ValidateUniqueNames(operations);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUniqueNames_WithDuplicateNames_ThrowsInvalidOperationException()
    {
        var operations = new[]
        {
            new BulkOperationInfo { Name = "import-users", OperationType = typeof(string) },
            new BulkOperationInfo { Name = "import-users", OperationType = typeof(int) },
            new BulkOperationInfo { Name = "export-data", OperationType = typeof(bool) }
        };

        var act = () => BulkOperationDiscoveryService.ValidateUniqueNames(operations);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate BulkOperation names*")
            .WithMessage("*import-users*");
    }

    [Fact]
    public void ValidateUniqueNames_WithDuplicateNames_CaseInsensitive_ThrowsInvalidOperationException()
    {
        var operations = new[]
        {
            new BulkOperationInfo { Name = "Import-Users", OperationType = typeof(string) },
            new BulkOperationInfo { Name = "import-users", OperationType = typeof(int) }
        };

        var act = () => BulkOperationDiscoveryService.ValidateUniqueNames(operations);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate BulkOperation names*");
    }

    [Fact]
    public void ValidateUniqueNames_WithEmptyCollection_DoesNotThrow()
    {
        var act = () => BulkOperationDiscoveryService.ValidateUniqueNames(Array.Empty<BulkOperationInfo>());

        act.Should().NotThrow();
    }
}
