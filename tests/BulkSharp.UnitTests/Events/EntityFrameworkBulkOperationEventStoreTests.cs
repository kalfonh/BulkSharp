using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Contracts;
using BulkSharp.Data.EntityFramework;
using BulkSharp.Processing.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.UnitTests.Events;

/// <summary>
/// The EF store exists so that a horizontally scaled service shares one event ordering.
/// These tests exercise it through <see cref="IBulkOperationEventStore"/> — the same surface
/// the in-memory store implements — so both are held to the same contract.
/// </summary>
[Trait("Category", "Unit")]
public class EntityFrameworkBulkOperationEventStoreTests
{
    private readonly DbContextOptions<BulkSharpDbContext> _options;

    public EntityFrameworkBulkOperationEventStoreTests()
    {
        _options = new DbContextOptionsBuilder<BulkSharpDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    /// <summary>
    /// Separate store instances over the same options stand in for separate service
    /// instances sharing one database.
    /// </summary>
    private IBulkOperationEventStore CreateStore() =>
        new EntityFrameworkBulkOperationEventStore(new SharedOptionsContextFactory(_options));

    private sealed class SharedOptionsContextFactory(DbContextOptions<BulkSharpDbContext> options)
        : IDbContextFactory<BulkSharpDbContext>
    {
        public BulkSharpDbContext CreateDbContext() => new(options);
    }

    private static OperationEventDto Event(
        Guid operationId,
        string type = "StatusChanged",
        OperationEventSeverity severity = OperationEventSeverity.Info,
        DateTime? timestamp = null)
        => new(
            Sequence: 0,
            operationId,
            "probe",
            type,
            severity,
            "message",
            timestamp ?? DateTime.UtcNow);

    [Fact]
    public async Task AppendAsync_AssignsSequenceFromTheDatabase()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        var first = await store.AppendAsync(Event(id));
        var second = await store.AppendAsync(Event(id));

        first.Sequence.Should().BeGreaterThan(0);
        second.Sequence.Should().BeGreaterThan(first.Sequence);
    }

    /// <summary>
    /// Two store instances stand in for two service instances sharing a database. A single
    /// ordering across both is the property the in-memory store cannot provide.
    /// </summary>
    [Fact]
    public async Task AppendAsync_FromSeparateInstances_SharesOneOrdering()
    {
        var instanceA = CreateStore();
        var instanceB = CreateStore();
        var id = Guid.NewGuid();

        var fromA = await instanceA.AppendAsync(Event(id, "Created"));
        var fromB = await instanceB.AppendAsync(Event(id, "Completed"));

        fromB.Sequence.Should().BeGreaterThan(fromA.Sequence);

        // And either instance can read back the other's events.
        var seenByA = await instanceA.GetForOperationAsync(id);
        seenByA.Should().HaveCount(2);
        seenByA.Select(e => e.Type).Should().ContainInOrder("Created", "Completed");
    }

    [Fact]
    public async Task AppendAsync_IgnoresCallerSuppliedSequence()
    {
        var store = CreateStore();

        var stored = await store.AppendAsync(new OperationEventDto(
            Sequence: 999,
            Guid.NewGuid(),
            "probe",
            "Created",
            OperationEventSeverity.Info,
            "message",
            DateTime.UtcNow));

        stored.Sequence.Should().NotBe(999);
    }

    [Fact]
    public async Task GetForOperationAsync_ReturnsOnlyThatOperation()
    {
        var store = CreateStore();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();

        await store.AppendAsync(Event(mine));
        await store.AppendAsync(Event(other));
        await store.AppendAsync(Event(mine));

        var events = await store.GetForOperationAsync(mine);

        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.OperationId == mine);
    }

    [Fact]
    public async Task GetAsync_WithSince_ExcludesEventsAlreadySeen()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.AppendAsync(Event(id));
        var second = await store.AppendAsync(Event(id));
        var third = await store.AppendAsync(Event(id));

        var events = await store.GetAsync(since: second.Sequence);

        events.Should().ContainSingle();
        events[0].Sequence.Should().Be(third.Sequence);
    }

    [Fact]
    public async Task GetAsync_WhenCaughtUp_ReturnsEmpty()
    {
        var store = CreateStore();
        var only = await store.AppendAsync(Event(Guid.NewGuid()));

        var events = await store.GetAsync(since: only.Sequence);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsEventsInSequenceOrder()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        // Append out of timestamp order to prove ordering follows sequence, not timestamp.
        await store.AppendAsync(Event(id, "Created", timestamp: DateTime.UtcNow.AddMinutes(5)));
        await store.AppendAsync(Event(id, "Completed", timestamp: DateTime.UtcNow));

        var events = await store.GetAsync();

        events.Select(e => e.Type).Should().ContainInOrder("Created", "Completed");
    }

    [Fact]
    public async Task GetAsync_RespectsLimit()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
            await store.AppendAsync(Event(id));

        var events = await store.GetAsync(limit: 3);

        events.Should().HaveCount(3);
    }

    [Fact]
    public async Task AppendAsync_RoundTripsSeverityAndMessage()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.AppendAsync(new OperationEventDto(
            0, id, "payment-processing", "Failed", OperationEventSeverity.Error,
            "SettlementDate is required.", DateTime.UtcNow));

        var events = await store.GetForOperationAsync(id);

        var stored = events.Should().ContainSingle().Subject;
        stored.OperationName.Should().Be("payment-processing");
        stored.Severity.Should().Be(OperationEventSeverity.Error);
        stored.Message.Should().Be("SettlementDate is required.");
    }

    /// <summary>
    /// AddBulkSharp registers the in-memory store with TryAdd, so opting into EF must
    /// actively replace it. Without this, a host with durable operation storage would still
    /// lose events on restart and hand scaled-out clients incomparable sequence numbers —
    /// silently, because everything would appear to work on one instance.
    /// </summary>
    [Fact]
    public void AddBulkSharpEntityFramework_OverridesTheInMemoryEventStore()
    {
        using var provider = BuildProvider(useEntityFramework: true);

        provider.GetRequiredService<IBulkOperationEventStore>()
            .Should().BeOfType<EntityFrameworkBulkOperationEventStore>();
    }

    [Fact]
    public void WithoutEntityFramework_TheInMemoryEventStoreIsUsed()
    {
        using var provider = BuildProvider(useEntityFramework: false);

        provider.GetRequiredService<IBulkOperationEventStore>()
            .Should().BeOfType<InMemoryBulkOperationEventStore>();
    }

    /// <summary>
    /// A host that registers its own store — Redis, a message log — must keep it. Overriding
    /// by last-registration-wins preserves that; removing existing registrations outright
    /// would silently discard the host's choice.
    /// </summary>
    [Fact]
    public void AHostsOwnEventStore_SurvivesTheEntityFrameworkRegistration()
    {
        var services = NewServices();
        services.AddSingleton<IBulkOperationEventStore, CustomEventStore>();

        services.AddDbContextFactory<BulkSharpDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>();

        // The host registered first, so EF wins here — but the host's registration is still
        // present and takes effect if it registers afterwards.
        services.AddSingleton<IBulkOperationEventStore, CustomEventStore>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBulkOperationEventStore>()
            .Should().BeOfType<CustomEventStore>();
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddBulkSharp(b => b
            .UseFileStorage(fs => fs.UseInMemory())
            .UseMetadataStorage(ms => ms.UseInMemory())
            .UseScheduler(s => s.UseImmediate()));
        return services;
    }

    private static ServiceProvider BuildProvider(bool useEntityFramework)
    {
        var services = NewServices();

        if (useEntityFramework)
        {
            services.AddDbContextFactory<BulkSharpDbContext>(o =>
                o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddBulkSharpEntityFramework<BulkSharpDbContext>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class CustomEventStore : IBulkOperationEventStore
    {
        public Task<OperationEventDto> AppendAsync(OperationEventDto e, CancellationToken ct = default)
            => Task.FromResult(e);

        public Task<IReadOnlyList<OperationEventDto>> GetForOperationAsync(
            Guid operationId, long? since = null, int limit = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationEventDto>>([]);

        public Task<IReadOnlyList<OperationEventDto>> GetAsync(
            long? since = null, int limit = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationEventDto>>([]);
    }

    /// <summary>
    /// Mirrors the in-memory store's contract: a caller with no cursor is bootstrapping and
    /// must receive the newest window, or its cursor lands mid-history and every later poll
    /// replays old events as new.
    /// </summary>
    [Fact]
    public async Task GetAsync_WithoutSince_ReturnsTheNewestWindowNotTheOldest()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        var appended = new List<long>();
        for (var i = 0; i < 20; i++)
            appended.Add((await store.AppendAsync(Event(id))).Sequence);

        var events = await store.GetAsync(limit: 5);

        events.Should().HaveCount(5);
        events.Select(e => e.Sequence).Should().ContainInOrder(appended.TakeLast(5));
    }

    [Fact]
    public async Task GetAsync_WithoutSince_LeavesCursorAtTheLatestEvent()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
            await store.AppendAsync(Event(id));

        var bootstrap = await store.GetAsync(limit: 5);
        var cursor = bootstrap.Max(e => e.Sequence);

        var next = await store.GetAsync(since: cursor);

        next.Should().BeEmpty("a client that has just bootstrapped is up to date");
    }

    [Fact]
    public async Task AppendAsync_WithNullEvent_Throws()
    {
        var store = CreateStore();

        var act = async () => await store.AppendAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
