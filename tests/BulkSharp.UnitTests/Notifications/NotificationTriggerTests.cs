using BulkSharp.Core.Domain.Notifications;

namespace BulkSharp.UnitTests.Notifications;

[Trait("Category", "Unit")]
public class NotificationTriggerTests
{
    [Fact]
    public void OnTerminal_IncludesCompletionFailureCompletedWithErrorsAndCancelled()
    {
        var trigger = NotificationTrigger.OnTerminal;

        trigger.HasFlag(NotificationTrigger.OnCompletion).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnFailure).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnCompletionWithErrors).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnCancelled).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnStatusChange).Should().BeFalse();
    }

    [Fact]
    public void Flags_CanBeCombined()
    {
        var trigger = NotificationTrigger.OnCompletion | NotificationTrigger.OnFailure;

        trigger.HasFlag(NotificationTrigger.OnCompletion).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnFailure).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnCancelled).Should().BeFalse();
    }

    [Fact]
    public void None_HasNoFlags()
    {
        var trigger = NotificationTrigger.None;
        trigger.Should().Be((NotificationTrigger)0);
    }

    [Fact]
    public void All_IncludesEveryFlag()
    {
        var trigger = NotificationTrigger.All;

        trigger.HasFlag(NotificationTrigger.OnCompletion).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnFailure).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnCompletionWithErrors).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnCancelled).Should().BeTrue();
        trigger.HasFlag(NotificationTrigger.OnStatusChange).Should().BeTrue();
    }
}
