# 001 · Publish through the outbox

## Context
The Subscriptions module stores subscriptions in its own database and has to tell other services about them. Saving the row and then publishing to the broker is a *dual write*: if the process or the broker fails between the two, the database and the rest of the system disagree forever. The transactional outbox closes that gap.

## Behavior
- `POST /api/subscriptions` with `customerEmail` and `plan` (`Basic`, `Pro` or `Team`) creates an active subscription.
- `POST /api/subscriptions/{id}/cancel` cancels it.
- Each change writes the subscription **and** an outbox message (`subscription.created.v1`, `subscription.cancelled.v1`) in the same `SaveChanges`, which is one database transaction. The module never talks to the broker.
- A background relay reads pending outbox messages and publishes them. A message is marked published only after the broker confirmed it.

## Acceptance criteria
- If the broker is down, `POST /api/subscriptions` still returns `201`; the message stays pending and is published once the broker is back.
- A failed publish increments `attempts`, records the error and schedules the next try with exponential backoff (1 s, 2 s, 4 s… capped by `Outbox:MaxBackoff`).
- After `Outbox:MaxAttempts` failures the message is dead-lettered. `POST /api/outbox/{id}/requeue` puts it back in the queue (`409` if it is not dead-lettered, `404` if unknown).
- Messages of the same subscription are published in the order they were written. A message waits while an earlier message of the same subscription is not yet published; other subscriptions are not blocked.
- Several relay instances can run at the same time: a batch is claimed with a lease (`locked_until`), so a message is not published twice by two healthy relays.
- Invalid email or plan → `400`; unknown subscription → `404`; cancelling twice → `409`.

## Out of scope
Payment, plan changes, authentication, cleanup of old published messages.
