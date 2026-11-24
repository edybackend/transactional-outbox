# 002 · Idempotent consumer (inbox)

## Context
The outbox gives **at-least-once** delivery: if the relay crashes after the broker confirmed a message but before it marked the message as published, the message is sent again. The consumer must make duplicates harmless.

## Behavior
- The Billing module consumes subscription events from its own database:
  - `subscription.created.v1` → opens a billing account and issues the first invoice for the plan's monthly price.
  - `subscription.cancelled.v1` → closes the billing account and voids its open invoices.
- Billing records each processed message id in an `inbox_messages` table **in the same transaction** as the effect.
- `GET /api/invoices` lists invoices.

## Acceptance criteria
- Delivering the same message twice issues one invoice. The second delivery is acknowledged and ignored.
- If two deliveries of the same message race, the primary key on `inbox_messages.message_id` lets only one commit.
- A cancellation for an unknown billing account is ignored. Messages of one subscription arrive in order (see 001), so this only happens for subscriptions created before Billing existed.
- If the handler throws, nothing is committed (neither the effect nor the inbox row), so a redelivery processes the message again.

## Out of scope
Payments, proration, retries of a poison message beyond the broker's dead-letter queue.
