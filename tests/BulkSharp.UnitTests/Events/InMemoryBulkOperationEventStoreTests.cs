using BulkSharp.Core.Contracts;
using BulkSharp.Processing.Events;

namespace BulkSharp.UnitTests.Events;

[Trait("Category", "Unit")]
public class InMemoryBulkOperationEventStoreTests
{
    private readonly InMemoryBulkOperationEventStore _sut = new();

    private Task<OperationEventDto> AppendAsync(
        Guid operationId,
        string type = "StatusChanged",
        OperationEventSeverity severity = OperationEventSeverity.Info)
        => _sut.AppendAsync(new OperationEventDto(
            Sequence: 0,
            operationId,
            "probe",
            type,
            severity,
            "message",
            DateTime.UtcNow));

    [Fact]
    public async Task AppendAsync_AssignsIncreasingSequences()
    {
        var id = Guid.NewGuid();

        var first = await AppendAsync(id);
        var second = await AppendAsync(id);

        first.Sequence.Should().Be(1);
        second.Sequence.Should().Be(2);
    }

    [Fact]
    public async Task AppendAsync_IgnoresCallerSuppliedSequence()
    {
        var stored = await _sut.AppendAsync(new OperationEventDto(
            Sequence: 999,
            Guid.NewGuid(),
            "probe",
            "Created",
            OperationEventSeverity.Info,
            "message",
            DateTime.UtcNow));

        stored.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task GetForOperationAsync_ReturnsOnlyThatOperation()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await AppendAsync(mine);
        await AppendAsync(other);
        await AppendAsync(mine);

        var events = await _sut.GetForOperationAsync(mine);

        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.OperationId == mine);
    }

    /// <summary>
    /// A client polls with the highest sequence it has seen. Returning events at or below
    /// that sequence would re-raise notifications the user already dismissed.
    /// </summary>
    [Fact]
    public async Task GetAsync_WithSince_ExcludesEventsAlreadySeen()
    {
        var id = Guid.NewGuid();
        await AppendAsync(id);
        var second = await AppendAsync(id);
        await AppendAsync(id);

        var events = await _sut.GetAsync(since: second.Sequence);

        events.Should().ContainSingle();
        events[0].Sequence.Should().Be(3);
    }

    [Fact]
    public async Task GetAsync_WhenCaughtUp_ReturnsEmpty()
    {
        var id = Guid.NewGuid();
        var only = await AppendAsync(id);

        var events = await _sut.GetAsync(since: only.Sequence);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_RespectsLimit()
    {
        var id = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
            await AppendAsync(id);

        var events = await _sut.GetAsync(limit: 3);

        events.Should().HaveCount(3);
    }

    /// <summary>
    /// The store is a bounded tail for driving a UI, not an audit log. Sequences must keep
    /// increasing across eviction so a client that falls behind observes a gap rather than
    /// silently re-reading recycled numbers.
    /// </summary>
    [Fact]
    public async Task AppendAsync_BeyondCapacity_EvictsOldestButKeepsSequencesIncreasing()
    {
        var id = Guid.NewGuid();
        for (var i = 0; i < 1050; i++)
            await AppendAsync(id);

        var events = await _sut.GetAsync(limit: 500);

        events.Should().NotBeEmpty();
        events[0].Sequence.Should().BeGreaterThan(1, "the earliest events should have been evicted");

        var last = await _sut.AppendAsync(new OperationEventDto(
            0, id, "probe", "Created", OperationEventSeverity.Info, "m", DateTime.UtcNow));
        last.Sequence.Should().Be(1051);
    }

    /// <summary>
    /// A client with no cursor is starting up and wants current state. Returning the oldest
    /// events would leave its cursor stranded in the middle of history, so every later poll
    /// would replay old events as new — observed in the dashboard as duplicated toasts.
    /// </summary>
    [Fact]
    public async Task GetAsync_WithoutSince_ReturnsTheNewestWindowNotTheOldest()
    {
        var id = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
            await AppendAsync(id);

        var events = await _sut.GetAsync(limit: 5);

        events.Should().HaveCount(5);
        events.Select(e => e.Sequence).Should().ContainInOrder(16, 17, 18, 19, 20);
    }

    [Fact]
    public async Task GetAsync_WithoutSince_LeavesCursorAtTheLatestEvent()
    {
        var id = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
            await AppendAsync(id);

        // Bootstrap exactly as a polling client does.
        var bootstrap = await _sut.GetAsync(limit: 5);
        var cursor = bootstrap.Max(e => e.Sequence);

        var next = await _sut.GetAsync(since: cursor);

        next.Should().BeEmpty("a client that has just bootstrapped is up to date");
    }

    [Fact]
    public async Task GetForOperationAsync_WithoutSince_ReturnsTheNewestWindow()
    {
        var id = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
            await AppendAsync(id);

        var events = await _sut.GetForOperationAsync(id, limit: 3);

        events.Select(e => e.Sequence).Should().ContainInOrder(18, 19, 20);
    }

    [Fact]
    public async Task AppendAsync_WithNullEvent_Throws()
    {
        var act = async () => await _sut.AppendAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
