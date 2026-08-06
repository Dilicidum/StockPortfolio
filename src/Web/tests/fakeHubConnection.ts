import type { AlertNotification } from '../src/alerts/alertsApi'

/**
 * A stand-in for the SignalR client, installed globally in `setup.ts`.
 *
 * jsdom has no WebSocket that reaches anything, and the authenticated layout opens the alert
 * connection on mount — so without this every protected-route test would hang or throw. It also
 * makes the two things that ARE ours drivable from a test: the pushed alert, and what happens
 * when a dropped connection comes back.
 *
 * Deliberately not a mock of the protocol. Asserting on SignalR's frames would be testing
 * Microsoft's code; what these tests care about is which of our callbacks ran.
 */
export interface FakeHubOptions {
  accessTokenFactory?: () => string | Promise<string>
  transport?: number
  skipNegotiation?: boolean
}

export class FakeHubConnection {
  static instances: FakeHubConnection[] = []

  static latest(): FakeHubConnection | null {
    return FakeHubConnection.instances.at(-1) ?? null
  }

  static reset(): void {
    FakeHubConnection.instances = []
  }

  url = ''
  options: FakeHubOptions = {}
  retryDelays: number[] = []
  started = false
  stopped = false

  private readonly methods = new Map<string, (payload: AlertNotification) => void>()
  private onReconnecting: (() => void) | null = null
  private onReconnected: (() => void) | null = null
  private onClosed: (() => void) | null = null

  on(methodName: string, handler: (payload: AlertNotification) => void): void {
    this.methods.set(methodName, handler)
  }

  onreconnecting(callback: () => void): void {
    this.onReconnecting = callback
  }

  onreconnected(callback: () => void): void {
    this.onReconnected = callback
  }

  onclose(callback: () => void): void {
    this.onClosed = callback
  }

  start(): Promise<void> {
    this.started = true
    return Promise.resolve()
  }

  stop(): Promise<void> {
    this.stopped = true
    this.onClosed?.()
    return Promise.resolve()
  }

  /** Server pushes one message. An unknown method name is dropped, exactly as SignalR drops it. */
  push(methodName: string, payload: AlertNotification): void {
    this.methods.get(methodName)?.(payload)
  }

  /** The connection dropped and SignalR's own retry got it back. */
  dropAndRecover(): void {
    this.onReconnecting?.()
    this.onReconnected?.()
  }

  /** The connection dropped and the retry schedule ran out. */
  dropForGood(): void {
    this.onReconnecting?.()
    this.onClosed?.()
  }
}

class FakeHubConnectionBuilder {
  private readonly connection = new FakeHubConnection()

  withUrl(url: string, options: FakeHubOptions): this {
    this.connection.url = url
    this.connection.options = options
    return this
  }

  withAutomaticReconnect(delays: number[]): this {
    this.connection.retryDelays = delays
    return this
  }

  build(): FakeHubConnection {
    FakeHubConnection.instances.push(this.connection)
    return this.connection
  }
}

/** The real enum's values, so a test asserting on the transport asserts on the real number. */
const HttpTransportType = {
  None: 0,
  WebSockets: 1,
  ServerSentEvents: 2,
  LongPolling: 4,
} as const

export const signalRModuleMock = { HubConnectionBuilder: FakeHubConnectionBuilder, HttpTransportType }
