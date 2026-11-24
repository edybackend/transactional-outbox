# 003 · Transports and the outage demo

## Context
The relay depends only on `IMessagePublisher`, and the consumer only on `IMessageHandler`. The transport is configuration.

## Behavior
- `Messaging:Transport = InMemory` (default for `dotnet run` and the tests): an in-process channel. `PUT /api/broker` with `{ "available": false }` simulates an outage and `{ "available": true }` ends it.
- `Messaging:Transport = RabbitMq` (Docker Compose): persistent messages on a durable topic exchange, publisher confirms, a durable queue for Billing with a dead-letter queue, and manual acknowledgements. The relay and the consumer reconnect on their own after the broker comes back.
- `GET /api/broker` reports the transport and whether it is reachable. In RabbitMQ mode, `PUT /api/broker` returns `409`; to simulate an outage you stop the container (`docker compose stop rabbitmq`).
- `GET /api/outbox` lists messages (filter by `status`), and `GET /api/outbox/stats` counts them per status.

## Acceptance criteria
- With the broker down, subscriptions keep being created, the outbox fills up and no invoice appears.
- When the broker comes back, the backlog drains in order and every subscription gets exactly one invoice.
- Startup does not fail if RabbitMQ is not reachable yet.

## Out of scope
Kafka or cloud brokers, change data capture.
