# Transactional Outbox · Inbox

[![CI](https://github.com/edybackend/transactional-outbox/actions/workflows/ci.yml/badge.svg)](https://github.com/edybackend/transactional-outbox/actions/workflows/ci.yml)
[Leia em português](README.pt-BR.md)

Reliable messaging between two services on .NET 10, with no distributed transaction.
**Subscriptions** saves a subscription *and* the message about it in one database transaction. A relay publishes the
message to RabbitMQ later. **Billing** consumes it idempotently and issues the invoice.

Stop the broker, keep creating subscriptions, start it again: nothing is lost and nothing is billed twice. CI runs
exactly that on every push.

## The problem: dual write

```csharp
await db.SaveChangesAsync();          // 1. the subscription is saved
await broker.PublishAsync(created);   // 2. 💥 broker down, timeout or process killed → Billing never hears about it
```

Two systems, two writes, no shared transaction. Swapping the two lines just moves the bug: the message goes out and
then the save fails. My [orders-microservices](https://github.com/edybackend/orders-microservices) project lists this
as a known trade-off. This repository closes that gap.

## Quick start

Requirements: Docker.

```bash
docker compose up --build
```

Open http://localhost:8080/scalar. Three sample subscriptions are already billed (one is cancelled). Then try the outage:

```bash
docker compose stop rabbitmq       # the broker is down
# create a few subscriptions in Scalar: 201 Created, they wait in GET /api/outbox?status=Pending
docker compose start rabbitmq      # within ~30 s (the backoff cap) the backlog drains and GET /api/invoices has them all
```

| What | Where |
|---|---|
| API reference (Scalar) | http://localhost:8080/scalar |
| RabbitMQ management | http://localhost:15672 (guest / guest) |
| Health check | http://localhost:8080/health |

There is also a [`requests.http`](requests.http) file for Visual Studio, Rider or VS Code.

<details>
<summary>Without Docker (.NET 10 SDK, in-memory broker)</summary>

```bash
dotnet run --project src/TransactionalOutbox.Api   # http://localhost:8080/scalar
```
`PUT /api/broker` with `{ "available": false }` simulates the outage, and `{ "available": true }` ends it.
</details>

## How it works

```mermaid
flowchart LR
    C[Client] -->|POST /api/subscriptions| S[Subscriptions]
    S -->|one transaction| DB1[(subscriptions<br/>+ outbox_messages)]
    R[Outbox relay] -->|claim batch with a lease| DB1
    R -->|publish, wait for confirm| MQ{{RabbitMQ}}
    R -->|mark published / schedule retry| DB1
    MQ -->|deliver| B[Billing consumer]
    B -->|one transaction| DB2[(invoices<br/>+ inbox_messages)]
```

1. **Write.** `SubscriptionService` adds the subscription and calls `db.AddToOutbox(...)`. One `SaveChanges`, so the row and the message are saved together or not at all. The module never talks to the broker.
2. **Relay.** A background service claims due messages with one `UPDATE` that sets a lease. Several relays can run without publishing the same message twice.
3. **Publish.** Persistent messages with **publisher confirms** and `mandatory`. A message is marked published only after RabbitMQ confirmed it was stored in a queue.
4. **Retry.** A failure records the error and schedules the next attempt with exponential backoff (1 s, 2 s, 4 s… capped at 30 s). After 25 attempts the message is dead-lettered and can be requeued from the API.
5. **Consume.** Billing checks its **inbox** table. The invoice and the inbox row are saved in one transaction, so a redelivered message is acknowledged and ignored.

### What the outbox looks like during an outage

```
GET /api/outbox?status=Pending
subscription.created.v1  attempts 4  next 15:43:04  "The RabbitMQ connection is down and recovering."
```

## Guarantees and how they are tested

| Guarantee | How | Test |
|---|---|---|
| No lost messages | outbox row in the same transaction as the change | broker down → `201`, message pending → broker back → invoice |
| No message published twice by two healthy relays | claim with a lease in one `UPDATE` | 3 relays at once over 60 messages → 60 publishes, all distinct |
| A crashed relay does not block forever | the lease expires and another relay takes over | `A_message_leased_by_another_relay_is_skipped_until_the_lease_expires` |
| Order per subscription | only the oldest unpublished message of a stream is eligible | `created` failing holds back its `cancelled`, never other subscriptions |
| Duplicates are harmless | inbox table, primary key on the message id | same message 2× and 5× concurrently → 1 invoice |
| Real broker behaviour | a CI job with a RabbitMQ service; another stops and restarts RabbitMQ with Compose | `Through_a_real_rabbitmq_a_subscription_is_billed_once`, CI `outage` job |

Two mutations prove the tests are not decorative: removing the per-stream rule makes the ordering test fail, and removing the lease condition makes the lease and concurrent-relay tests fail.

## API

| Method | Route | What it does |
|---|---|---|
| `POST` | `/api/subscriptions` | start (`Basic`, `Pro`, `Team`) → `subscription.created.v1` in the outbox |
| `POST` | `/api/subscriptions/{id}/cancel` | cancel → `subscription.cancelled.v1` (`409` if already cancelled) |
| `GET` | `/api/subscriptions`, `/api/subscriptions/{id}` | subscriptions |
| `GET` | `/api/invoices?subscriptionId=` | invoices issued by Billing |
| `GET` | `/api/outbox?status=&take=`, `/api/outbox/stats` | messages with attempts, next attempt and last error |
| `POST` | `/api/outbox/{messageId}/requeue` | requeue a dead-lettered message |
| `GET` / `PUT` | `/api/broker` | transport status / simulate an outage (in-memory only) |

Errors are RFC 9457 problem details: `400` invalid input, `404` unknown id, `409` conflict.

## Tech stack

| Area | Choice |
|---|---|
| Back-end | .NET 10, minimal APIs, OpenAPI + Scalar |
| Persistence | EF Core 10 + SQLite, one database per module, WAL mode |
| Messaging | RabbitMQ (`RabbitMQ.Client` 7: publisher confirms, manual acks, DLQ, automatic recovery) or an in-memory broker |
| Quality | xUnit v3, WebApplicationFactory, NetArchTest, Roslyn analyzers (warnings as errors) |
| Delivery | Multi-stage non-root image, Docker Compose, GitHub Actions |

## Project structure

```
src/
  Subscriptions/            producer module: subscription, use cases, DbContext with the outbox table
  Subscriptions.Contracts/  integration events shared with consumers (the only shared code)
  Billing/                  consumer module: invoices, idempotent handler, inbox table
  Outbox/                   generic outbox: message, retry policy, leased dispatcher, relay, admin
  Messaging/                transport abstractions + in-memory broker
  Messaging.RabbitMq/       RabbitMQ publisher, consumer and topology
  TransactionalOutbox.Api/  endpoints, problem details mapping, composition root, sample data
tests/                      unit, integration (SQLite, API, RabbitMQ) and architecture tests
specs/                      feature specs (spec-first workflow)
```

## Design decisions

- **Outbox in the module's own database.** The guarantee comes from the local transaction; the outbox library is generic over any `DbContext`.
- **Polling, not change data capture.** A 500 ms poll is simple and portable. CDC (Debezium) would remove the polling at the cost of more infrastructure.
- **Lease instead of row locks.** SQLite has no `SELECT … FOR UPDATE SKIP LOCKED`, so a single `UPDATE` claims the batch. On PostgreSQL I would use `SKIP LOCKED`.
- **Per-stream ordering, not global ordering.** A failing message holds back only its own subscription, and a poison message cannot stop the whole outbox.
- **At-least-once plus an inbox, not "exactly-once".** Exactly-once delivery across a network is not available; effectively-once processing is.
- **The broker cannot block startup.** The connection opens on first use, and the client's automatic recovery restores the publisher and the consumer after an outage.

## Tests

```bash
dotnet test                                                  # unit, integration and architecture
RABBITMQ_URL=amqp://guest:guest@localhost:5672 dotnet test   # also through a real broker
```

## Known trade-offs

- **Polling latency.** Up to one polling interval (500 ms) between the commit and the publish.
- **Published rows are kept.** A production outbox would delete or archive published messages after a retention period.
- **Head-of-line blocking per stream.** A dead-lettered message holds back the later messages of its subscription until it is requeued. That is intended, since publishing them out of order would be worse.
- **Both modules run in one process** so the demo is one container. They share no code or database, so they can be split into two services with no change to the outbox or the consumer.

## Working with an AI agent

The one mistake this repository exists to prevent is publishing straight to the broker after `SaveChanges`. That is exactly the shortcut a code assistant would take, so a hook in [`.claude/`](.claude) refuses it in the Subscriptions module, and another protects the published message contracts. The rest of the rules are in [`CLAUDE.md`](CLAUDE.md).

## License

[MIT](LICENSE)
