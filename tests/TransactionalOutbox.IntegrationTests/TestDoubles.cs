using System.Collections.Concurrent;
using Messaging;

namespace TransactionalOutbox.IntegrationTests;

/// <summary>A clock the test moves by hand, so backoff and leases are deterministic.</summary>
public sealed class MovableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Records what was published. Can be switched off (outage) or fail specific messages.</summary>
public sealed class RecordingPublisher : IMessagePublisher
{
    public ConcurrentQueue<MessageEnvelope> Published { get; } = new();

    public bool Available { get; set; } = true;

    public Func<MessageEnvelope, bool> Rejects { get; set; } = _ => false;

    /// <summary>Simulates "the broker confirmed, then the relay crashed before saving": the message is sent but the call fails.</summary>
    public Func<MessageEnvelope, bool> FailsAfterSending { get; set; } = _ => false;

    public async Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (!Available || Rejects(message))
        {
            throw new BrokerUnavailableException("broker down (test)");
        }

        Published.Enqueue(message);
        if (FailsAfterSending(message))
        {
            throw new IOException("connection lost after the broker confirmed (test)");
        }
    }
}

/// <summary>A throw-away SQLite file. Pooling is off so the file can be deleted at the end.</summary>
public sealed class TempDatabase : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"outbox-tests-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={Path};Pooling=False";

    public void Dispose()
    {
        foreach (var file in new[] { Path, Path + "-wal", Path + "-shm" })
        {
            File.Delete(file);
        }
    }
}

public static class Eventually
{
    /// <summary>Polls until the condition holds; background services make the outcome asynchronous.</summary>
    public static async Task TrueAsync(Func<Task<bool>> condition, string because, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out waiting until {because}.");
    }
}
