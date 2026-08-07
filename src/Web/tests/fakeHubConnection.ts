import type { AlertNotification } from '../src/alerts/alertsApi'

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

  withAutomaticReconnect(delays: number[]): this {
    this.connection.retryDelays = delays
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
