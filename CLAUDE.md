# Engineering rules for AI agents

This file is loaded by Claude Code in every session. It defines how changes are made in this repository.
Hooks in `.claude/settings.json` enforce the parts that can be automated.

## Workflow
1. **Spec first.** A new behavior starts as a spec in `specs/NNN-name.md` (context, behavior, acceptance criteria, out of scope). Code follows the spec.
2. **Small steps.** One concern per commit, [Conventional Commits](https://www.conventionalcommits.org/) (`feat(outbox): ...`, `fix(billing): ...`).
3. **Definition of done** = the CI gates pass locally: `dotnet build` (warnings are errors), `dotnet test`, `dotnet format --verify-no-changes`. The Stop hook blocks finishing while they are red. Set `RABBITMQ_URL` to also run the real-broker test.

## Module boundaries (enforced by `tests/TransactionalOutbox.ArchitectureTests`)
- `Subscriptions` (producer) and `Billing` (consumer) each own their database. They share **only** `Subscriptions.Contracts`.
- `Outbox` is generic: it knows no module and no broker client. `Messaging` holds the transport abstractions; `Messaging.RabbitMq` is one implementation.
- `TransactionalOutbox.Api` wires the modules and maps HTTP. Endpoints call services (`SubscriptionService`, `OutboxAdmin`, `BillingQueries`), never a `DbContext` directly.

## Messaging rules
- **Never publish from a module.** A change and its integration event are saved together: `db.AddToOutbox(...)` followed by one `SaveChanges`. Calling the broker after `SaveChanges` is a dual write. A hook blocks `IMessagePublisher` in `src/Subscriptions`.
- Delivery is **at-least-once**. Every consumer deduplicates by message id with an inbox row written in the same transaction as its effect.
- A message is marked published only after the broker confirmed it (publisher confirms + `mandatory`).
- Messages of one stream (`StreamKey` = the entity id) are published in order. Keep that key stable.
- **Published messages are a contract.** Never rename or remove a field or a type name in `SubscriptionEvents.cs`; add an optional field or a new `.v2` type. A hook blocks removals.
- Acknowledge only after the handler committed; a handler failure is rejected without requeue (dead-letter queue).

## C# conventions
- Classes `sealed` by default; file-scoped namespaces; `TimeProvider` instead of `DateTime.Now`; `Guid.CreateVersion7()` for ids.
- Expected failures are values (`Result<T>` with a `Failure`); HTTP status codes are decided only in `FailureResults`.
- No MediatR/MassTransit. Logging with `[LoggerMessage]`.

## Never
- Edit the SQLite files by hand; change data through the API. Blocked by hook.
- Read or write secrets (`.env`, keys). Blocked by permissions and hook.
- Disable an analyzer or a test to make the build pass.
- Add a dependency without a reason stated in the commit message.
