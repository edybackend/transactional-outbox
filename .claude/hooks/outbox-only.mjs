// PreToolUse hook: the Subscriptions module must never publish to the broker. Publishing after SaveChanges
// is the dual write the outbox exists to prevent; every message goes through db.AddToOutbox(...).
import { readFileSync } from 'node:fs'

const input = JSON.parse(readFileSync(0, 'utf8'))
const filePath = (input.tool_input?.file_path ?? '').replaceAll('\\', '/')
if (!/\/src\/Subscriptions\/.*\.cs$/.test(filePath)) process.exit(0)

const written = input.tool_input?.new_string ?? input.tool_input?.content ?? ''
const forbidden = [/\bIMessagePublisher\b/, /\bPublishAsync\s*\(/, /\busing\s+Messaging\b/, /\bRabbitMQ\b/]
const hit = forbidden.find((pattern) => pattern.test(written))
if (hit) {
  console.error(
    `Blocked: ${filePath} would publish to the broker directly (${hit}). ` +
      'Stage the event with db.AddToOutbox(...) in the same SaveChanges; the relay publishes it.',
  )
  process.exit(2)
}
