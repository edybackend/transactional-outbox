using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Outbox;
using Subscriptions.Persistence;

namespace TransactionalOutbox.IntegrationTests;

/// <summary>The relay's single pass against a real SQLite outbox, with a recording publisher and a hand-moved clock.</summary>
public sealed class OutboxDispatcherTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly MovableClock _clock = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingPublisher _publisher = new();
    private readonly OutboxOptions _options = new()
    {
        BatchSize = 10,
        MaxAttempts = 3,
        BaseBackoff = TimeSpan.FromSeconds(1),
        MaxBackoff = TimeSpan.FromSeconds(30),
        LeaseDuration = TimeSpan.FromSeconds(30),
    };

    public OutboxDispatcherTests()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publishes_pending_messages_in_order_and_marks_them_published()
    {
        await WriteAsync(("a", "a.1"), ("b", "b.1"), ("c", "c.1"));

        var processed = await DispatchAsync();

        Assert.Equal(3, processed);
        Assert.Equal(["a.1", "b.1", "c.1"], _publisher.Published.Select(m => m.Type));
        Assert.All(await AllAsync(), m => Assert.Equal(OutboxStatus.Published, m.Status));
        Assert.Equal(0, await DispatchAsync()); // nothing left
    }

    [Fact]
    public async Task While_the_broker_is_down_messages_wait_with_backoff_and_go_out_when_it_returns()
    {
        await WriteAsync(("a", "a.1"));
        _publisher.Available = false;

        await DispatchAsync();
        var failed = Assert.Single(await AllAsync());
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(1), failed.NextAttemptAt);
        Assert.Contains("broker down", failed.LastError, StringComparison.Ordinal);

        _publisher.Available = true;
        Assert.Equal(0, await DispatchAsync()); // not due yet: the backoff is respected

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await DispatchAsync());
        Assert.Equal(OutboxStatus.Published, Assert.Single(await AllAsync()).Status);
    }

    [Fact]
    public async Task After_the_last_attempt_the_message_is_dead_lettered_and_can_be_requeued()
    {
        await WriteAsync(("a", "a.1"));
        _publisher.Available = false;
        for (var attempt = 0; attempt < _options.MaxAttempts; attempt++)
        {
            await DispatchAsync();
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var dead = Assert.Single(await AllAsync());
        Assert.Equal(OutboxStatus.DeadLettered, dead.Status);
        Assert.Equal(0, await DispatchAsync());

        _publisher.Available = true;
        await using (var db = NewContext())
        {
            Assert.Equal(RequeueOutcome.Requeued, await new OutboxAdmin<SubscriptionsDbContext>(db, _clock).RequeueAsync(dead.MessageId, Ct));
        }

        Assert.Equal(1, await DispatchAsync());
        Assert.Equal(OutboxStatus.Published, Assert.Single(await AllAsync()).Status);
    }

    [Fact]
    public async Task A_failing_message_holds_back_its_own_stream_only()
    {
        await WriteAsync(("s1", "s1.created"), ("s1", "s1.cancelled"), ("s2", "s2.created"));
        _publisher.Rejects = m => m.Type == "s1.created";

        await DispatchAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await DispatchAsync();

        Assert.Equal(["s2.created"], _publisher.Published.Select(m => m.Type)); // s1.cancelled never overtakes s1.created

        _publisher.Rejects = _ => false;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await DispatchAsync();
        await DispatchAsync();

        Assert.Equal(["s2.created", "s1.created", "s1.cancelled"], _publisher.Published.Select(m => m.Type));
    }

    [Fact]
    public async Task A_message_leased_by_another_relay_is_skipped_until_the_lease_expires()
    {
        await WriteAsync(("a", "a.1"));
        await using (var db = NewContext())
        {
            // Another relay claimed it and then crashed.
            await db.Set<OutboxMessage>().ExecuteUpdateAsync(
                set => set.SetProperty(m => m.LockId, Guid.NewGuid()).SetProperty(m => m.LockedUntil, _clock.GetUtcNow().AddSeconds(30)),
                Ct);
        }

        Assert.Equal(0, await DispatchAsync());

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, await DispatchAsync());
    }

    [Fact]
    public async Task A_crash_after_the_broker_confirmed_sends_the_message_again_with_the_same_id()
    {
        await WriteAsync(("a", "a.1"));
        _publisher.FailsAfterSending = _ => true;
        await DispatchAsync();

        _publisher.FailsAfterSending = _ => false;
        _clock.Advance(TimeSpan.FromSeconds(1));
        await DispatchAsync();

        Assert.Equal(2, _publisher.Published.Count); // at-least-once…
        Assert.Single(_publisher.Published.Select(m => m.MessageId).Distinct()); // …with a stable id, so consumers can deduplicate
    }

    [Fact]
    public async Task Two_relays_running_at_the_same_time_publish_each_message_once()
    {
        var streams = Enumerable.Range(1, 60).Select(i => ($"stream-{i}", $"type-{i}")).ToArray();
        await WriteAsync(streams);

        async Task RelayAsync()
        {
            while (await DispatchAsync() > 0)
            {
            }
        }

        await Task.WhenAll(RelayAsync(), RelayAsync(), RelayAsync());

        Assert.Equal(60, _publisher.Published.Count);
        Assert.Equal(60, _publisher.Published.Select(m => m.MessageId).Distinct().Count());
        Assert.All(await AllAsync(), m => Assert.Equal(OutboxStatus.Published, m.Status));
    }

    public void Dispose() => _database.Dispose();

    private SubscriptionsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SubscriptionsDbContext>().UseSqlite(_database.ConnectionString).Options);

    private async Task WriteAsync(params (string Stream, string Type)[] messages)
    {
        await using var db = NewContext();
        foreach (var (stream, type) in messages)
        {
            db.AddToOutbox(type, stream, "{}", _clock.GetUtcNow());
        }

        await db.SaveChangesAsync(Ct);
    }

    private async Task<int> DispatchAsync()
    {
        await using var db = NewContext();
        var dispatcher = new OutboxDispatcher<SubscriptionsDbContext>(
            db, _publisher, _clock, Options.Create(_options), NullLogger<OutboxDispatcher<SubscriptionsDbContext>>.Instance);
        return await dispatcher.DispatchAsync(Ct);
    }

    private async Task<List<OutboxMessage>> AllAsync()
    {
        await using var db = NewContext();
        return await db.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.Sequence).ToListAsync(Ct);
    }
}
