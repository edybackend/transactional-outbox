namespace Messaging;

/// <summary>
/// A message as it travels through the broker. <see cref="MessageId"/> is stable across redeliveries,
/// so consumers can deduplicate; <see cref="StreamKey"/> identifies the entity whose messages must stay in order.
/// </summary>
public sealed record MessageEnvelope(
    Guid MessageId,
    string Type,
    string StreamKey,
    string Payload,
    DateTimeOffset OccurredAt);
