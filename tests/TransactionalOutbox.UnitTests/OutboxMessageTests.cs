using Outbox;

namespace TransactionalOutbox.UnitTests;

public class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly RetryPolicy Policy = new(MaxAttempts: 3, BaseDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(30));

    private static OutboxMessage NewMessage() => OutboxMessage.Create("thing.happened.v1", "stream-1", "{}", Now);

    [Fact]
    public void A_new_message_is_pending_and_due_now()
    {
        var message = NewMessage();

        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(Now, message.NextAttemptAt);
        Assert.Equal(0, message.Attempts);
        Assert.NotEqual(Guid.Empty, message.MessageId);
    }

    [Fact]
    public void A_failure_records_the_error_and_schedules_the_next_attempt()
    {
        var message = NewMessage();

        message.MarkFailed("broker down", Now, Policy);

        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Equal("broker down", message.LastError);
        Assert.Equal(Now.AddSeconds(1), message.NextAttemptAt);
        Assert.Null(message.LockedUntil);
    }

    [Fact]
    public void The_last_allowed_failure_dead_letters_the_message()
    {
        var message = NewMessage();

        for (var i = 0; i < Policy.MaxAttempts; i++)
        {
            message.MarkFailed("broker down", Now, Policy);
        }

        Assert.Equal(OutboxStatus.DeadLettered, message.Status);
        Assert.Equal(3, message.Attempts);
    }

    [Fact]
    public void A_long_error_is_truncated()
    {
        var message = NewMessage();

        message.MarkFailed(new string('x', 2000), Now, Policy);

        Assert.Equal(500, message.LastError!.Length);
    }

    [Fact]
    public void Publishing_clears_the_last_error()
    {
        var message = NewMessage();
        message.MarkFailed("broker down", Now, Policy);

        message.MarkPublished(Now.AddSeconds(2));

        Assert.Equal(OutboxStatus.Published, message.Status);
        Assert.Equal(Now.AddSeconds(2), message.PublishedAt);
        Assert.Equal(2, message.Attempts);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void Only_a_dead_lettered_message_can_be_requeued()
    {
        var message = NewMessage();
        Assert.False(message.Requeue(Now));

        for (var i = 0; i < Policy.MaxAttempts; i++)
        {
            message.MarkFailed("broker down", Now, Policy);
        }

        Assert.True(message.Requeue(Now.AddMinutes(5)));
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Equal(Now.AddMinutes(5), message.NextAttemptAt);
    }
}
