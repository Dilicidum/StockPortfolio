/**
 * jsdom implements no `EventSource` at all — it is not stubbed, it is simply absent — so
 * every test that mounts a protected route would otherwise die inside the alert stream
 * hook. This is a jsdom gap being filled, in the same category as the `window.scrollTo`
 * stub in `setup.ts`, and it is deliberately a real object rather than a `vi.fn()`: the
 * hook's whole job is what it does with `open`, `error` and named events, and a spy that
 * records a constructor call proves none of that.
 *
 * Installed once in `tests/setup.ts`, so an authenticated route mounts the same way in
 * every file. `reset()` between tests clears the registry without replacing the global,
 * which keeps the two halves of a connection — the one a test created and the one the
 * hook is holding — from ever belonging to different classes.
 */
type Listener = (event: Event) => void

export class FakeEventSource {
  /** Every source ever constructed, in order. Length is the assertion for "opened twice". */
  static instances: FakeEventSource[] = []

  static reset(): void {
    FakeEventSource.instances = []
  }

  /** The connection the hook is currently holding, which is always the newest one. */
  static latest(): FakeEventSource | undefined {
    return FakeEventSource.instances.at(-1)
  }

  /** Constructed and not closed — the only figure that answers "how many are we holding?" */
  static get live(): FakeEventSource[] {
    return FakeEventSource.instances.filter((source) => !source.closed)
  }

  readonly url: string
  readyState = 0
  closed = false
  withCredentials = false

  onopen: Listener | null = null
  onerror: Listener | null = null
  onmessage: Listener | null = null

  private readonly listeners = new Map<string, Set<Listener>>()

  constructor(url: string) {
    this.url = url
    FakeEventSource.instances.push(this)
  }

  addEventListener(type: string, listener: Listener): void {
    const existing = this.listeners.get(type)
    if (existing) existing.add(listener)
    else this.listeners.set(type, new Set([listener]))
  }

  removeEventListener(type: string, listener: Listener): void {
    this.listeners.get(type)?.delete(listener)
  }

  close(): void {
    this.closed = true
    this.readyState = 2
  }

  // ── the driving half: what a test does to this connection ──────────────────

  /** The server accepted the ticket and the stream is up. */
  emitOpen(): void {
    this.readyState = 1
    this.onopen?.(new Event('open'))
  }

  /** A named event with a JSON body — `alert` and `ping` are the only two that exist. */
  emit(type: string, data: unknown): void {
    const event = new MessageEvent(type, { data: JSON.stringify(data) })

    for (const listener of this.listeners.get(type) ?? []) listener(event)
    if (type === 'message') this.onmessage?.(event)
  }

  /** The connection dropped. A real `EventSource` would start retrying here; the hook must not let it. */
  emitError(): void {
    this.readyState = 0
    this.onerror?.(new Event('error'))
  }
}

export function installFakeEventSource(): void {
  globalThis.EventSource = FakeEventSource as unknown as typeof EventSource
}
