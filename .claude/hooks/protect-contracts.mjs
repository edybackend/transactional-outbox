// PreToolUse hook: published messages are a contract with other services. Removing or renaming a field
// or changing a message type name breaks consumers, so it needs a new versioned type instead.
import { readFileSync } from 'node:fs'

const input = JSON.parse(readFileSync(0, 'utf8'))
const filePath = (input.tool_input?.file_path ?? '').replaceAll('\\', '/')
if (!filePath.endsWith('src/Subscriptions.Contracts/SubscriptionEvents.cs')) process.exit(0)

const before = (() => {
  try {
    return readFileSync(filePath, 'utf8')
  } catch {
    return ''
  }
})()
const oldText = input.tool_input?.old_string
const after = oldText !== undefined ? before.replace(oldText, input.tool_input?.new_string ?? '') : (input.tool_input?.content ?? '')

// "record Name(field list)" → "Name.Field", plus every message type string ("subscription.created.v1").
const members = (source) =>
  new Set([
    ...[...source.matchAll(/record\s+(\w+)\s*\(([^)]*)\)/g)].flatMap(([, record, args]) =>
      args.split(',').map((arg) => `${record}.${arg.trim().split(/\s+/).pop()}`),
    ),
    ...[...source.matchAll(/"([a-z]+(?:\.[a-z]+)+\.v\d+)"/g)].map(([, type]) => `type ${type}`),
  ])

const removed = [...members(before)].filter((member) => !members(after).has(member))
if (removed.length > 0) {
  console.error(
    `Blocked: this edit removes or renames published contract members (${removed.join(', ')}). ` +
      'Consumers would break. Add an optional field or a new ".v2" message type instead, or ask the developer.',
  )
  process.exit(2)
}
