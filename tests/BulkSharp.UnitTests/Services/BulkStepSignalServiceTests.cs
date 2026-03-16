using BulkSharp.Core.Exceptions;
using BulkSharp.Processing.Services;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class BulkStepSignalServiceTests
{
    private readonly BulkStepSignalService _service = new();

    [Fact]
    public void RegisterWaiter_NewKey_ReturnsTaskCompletionSource()
    {
        // Act
        var tcs = _service.RegisterWaiter("signal-1");

        // Assert
        Assert.NotNull(tcs);
        tcs.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void RegisterWaiter_DuplicateKey_Throws()
    {
        // Arrange
        _service.RegisterWaiter("signal-1");

        // Act & Assert
        var act = () => _service.RegisterWaiter("signal-1");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task TrySignal_WhenWaiterExists_CompletesSuccessfully()
    {
        // Arrange
        var tcs = _service.RegisterWaiter("signal-1");

        // Act
        var result = _service.TrySignal("signal-1");

        // Assert
        result.Should().BeTrue();
        tcs.Task.IsCompletedSuccessfully.Should().BeTrue();
        var value = await tcs.Task;
        value.Should().BeTrue();
    }

    [Fact]
    public void TrySignal_WhenNoWaiter_ReturnsFalse()
    {
        // Act
        var result = _service.TrySignal("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TrySignalFailure_WhenWaiterExists_SetsException()
    {
        // Arrange
        var tcs = _service.RegisterWaiter("signal-1");

        // Act
        var result = _service.TrySignalFailure("signal-1", "External process failed");

        // Assert
        result.Should().BeTrue();
        tcs.Task.IsFaulted.Should().BeTrue();
        tcs.Task.Exception!.InnerException.Should().BeOfType<BulkStepSignalFailureException>()
            .Which.SignalKey.Should().Be("signal-1");
    }

    [Fact]
    public void TrySignalFailure_WhenNoWaiter_ReturnsFalse()
    {
        // Act
        var result = _service.TrySignalFailure("nonexistent", "error");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RemoveWaiter_CancelsTaskCompletionSource()
    {
        // Arrange
        var tcs = _service.RegisterWaiter("signal-1");

        // Act
        _service.RemoveWaiter("signal-1");

        // Assert
        tcs.Task.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public void RemoveWaiter_WhenNoWaiter_DoesNotThrow()
    {
        // Act & Assert
        var act = () => _service.RemoveWaiter("nonexistent");
        act.Should().NotThrow();
    }

    [Fact]
    public void GetOrRegisterWaiter_NewKey_CreatesWaiter()
    {
        // Act
        var tcs = _service.GetOrRegisterWaiter("signal-1");

        // Assert
        Assert.NotNull(tcs);
        tcs.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void GetOrRegisterWaiter_ExistingKey_ReturnsSameWaiter()
    {
        // Arrange
        var first = _service.GetOrRegisterWaiter("signal-1");

        // Act
        var second = _service.GetOrRegisterWaiter("signal-1");

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public void TrySignal_AfterRemoveWaiter_ReturnsFalse()
    {
        // Arrange
        _service.RegisterWaiter("signal-1");
        _service.RemoveWaiter("signal-1");

        // Act
        var result = _service.TrySignal("signal-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TrySignal_CalledTwice_SecondReturnsFalse()
    {
        // Arrange
        _service.RegisterWaiter("signal-1");

        // Act
        var first = _service.TrySignal("signal-1");
        var second = _service.TrySignal("signal-1");

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
    }
}
