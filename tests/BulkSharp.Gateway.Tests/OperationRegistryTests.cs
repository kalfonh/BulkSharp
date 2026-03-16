using BulkSharp.Gateway.Registry;

namespace BulkSharp.Gateway.Tests;

[Trait("Category", "Unit")]
public class OperationRegistryTests
{
    private readonly OperationRegistry _sut = new();

    [Fact]
    public void LookupService_AfterUpdate_ReturnsCorrectService()
    {
        _sut.UpdateOperations("service-a", ["import-users", "import-devices"]);

        _sut.LookupService("import-users").Should().Be("service-a");
        _sut.LookupService("import-devices").Should().Be("service-a");
    }

    [Fact]
    public void LookupService_Unknown_ReturnsNull()
    {
        _sut.UpdateOperations("service-a", ["import-users"]);

        _sut.LookupService("non-existent").Should().BeNull();
    }

    [Fact]
    public void UpdateOperations_DuplicateName_DifferentService_Throws()
    {
        _sut.UpdateOperations("service-a", ["op1"]);

        var act = () => _sut.UpdateOperations("service-b", ["op1"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'op1'*'service-a'*'service-b'*");
    }

    [Fact]
    public void UpdateOperations_SameName_SameService_DoesNotThrow()
    {
        _sut.UpdateOperations("service-a", ["op1"]);

        var act = () => _sut.UpdateOperations("service-a", ["op1"]);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetAllOperations_ReturnsAll()
    {
        _sut.UpdateOperations("service-a", ["op1", "op2"]);
        _sut.UpdateOperations("service-b", ["op3"]);

        var all = _sut.GetAllOperations();

        all.Should().HaveCount(3);
        all.Should().Contain(e => e.OperationName == "op1" && e.ServiceName == "service-a");
        all.Should().Contain(e => e.OperationName == "op2" && e.ServiceName == "service-a");
        all.Should().Contain(e => e.OperationName == "op3" && e.ServiceName == "service-b");
    }

    [Fact]
    public void LookupService_IsCaseInsensitive()
    {
        _sut.UpdateOperations("service-a", ["Import-Users"]);

        _sut.LookupService("import-users").Should().Be("service-a");
        _sut.LookupService("IMPORT-USERS").Should().Be("service-a");
    }
}
