import type { AlertNotification } from '../src/alerts/alertsApi'

export interface FakeHubOptions {
  accessTokenFactory?: () => string | Promise<string>
  transport?: number
  skipNegotiation?: boolean
}

export interface FakeRetryContext {
  previousRetryCount: number
  elapsedMilliseconds: number
  retryReason: Error
}

export interface FakeRetryPolicy {
  nextRetryDelayInMilliseconds(context: FakeRetryContext): number | null
}

function policyFromDelays(delays: number[]): FakeRetryPolicy {
  const table: (number | null)[] = [...delays, null]

  return {
    nextRetryDelayInMilliseconds: ({ previousRetryCount }) => table[previousRetryCount] ?? null,
  }
}

export class FakeHubConnection {
  static instances: FakeHubConnection[] = []

  static rejectFirstStarts = 0

  static latest(): FakeHubConnection | null {
    return FakeHubConnection.instances.at(-1) ?? null
  }

  static reset(): void {
    FakeHubConnection.instances = []
    FakeHubConnection.rejectFirstStarts = 0
  }

  url = ''
  options: FakeHubOptions = {}
  retryPolicy: FakeRetryPolicy = policyFromDelays([])
  started = false
  stopped = false
  startAttempts = 0

  private rejectsLeft = FakeHubConnection.rejectFirstStarts

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
    this.startAttempts += 1

    if (this.rejectsLeft > 0) {
      this.rejectsLeft -= 1
      return Promise.reject(new Error('The hub refused the connection.'))
    }

    this.started = true
    return Promise.resolve()
  }

  askForRetryDelays(attempts: number): (number | null)[] {
    const delays: (number | null)[] = []

    for (let previousRetryCount = 0; previousRetryCount < attempts; previousRetryCount += 1) {
      delays.push(
        this.retryPolicy.nextRetryDelayInMilliseconds({
          previousRetryCount,
          elapsedMilliseconds: previousRetryCount * 1_000,
          retryReason: new Error('The connection dropped.'),
        }),
      )
    }

    return delays
  }

  stop(): Promise<void> {
    this.stopped = true
    this.onClosed?.()
    return Promise.resolve()
  }

  push(methodName: string, payload: AlertNotification): void {
    this.methods.get(methodName)?.(payload)
  }

  dropAndRecover(): void {
    this.onReconnecting?.()
    this.onReconnected?.()
  }

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

  withAutomaticReconnect(policy: number[] | FakeRetryPolicy): this {
    this.connection.retryPolicy = Array.isArray(policy) ? policyFromDelays(policy) : policy
    return this
  }

  build(): FakeHubConnection {
    FakeHubConnection.instances.push(this.connection)
    return this.connection
  }
}

const HttpTransportType = {
  None: 0,
  WebSockets: 1,
  ServerSentEvents: 2,
  LongPolling: 4,
} as const

export const signalRModuleMock = { HubConnectionBuilder: FakeHubConnectionBuilder, HttpTransportType }
