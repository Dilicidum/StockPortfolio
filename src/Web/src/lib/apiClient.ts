import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setTokens,
  type TokenPair,
} from './tokenStore'

export const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? ''

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null, fallbackMessage: string) {
    super(problem?.detail || problem?.title || fallbackMessage)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  get fieldErrors(): Record<string, string[]> {
    const raw = this.problem?.errors
    if (!raw) return {}

    const out: Record<string, string[]> = {}
    for (const [key, messages] of Object.entries(raw)) {
      const camel = key.charAt(0).toLowerCase() + key.slice(1)
      out[camel] = messages
    }
    return out
  }
}

interface RequestOptions {
  method?: string
  body?: unknown
  authenticated?: boolean
  signal?: AbortSignal
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return null

  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

async function readJson<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T

  const text = await response.text()
  if (text.length === 0) return undefined as T

  return JSON.parse(text) as T
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const headers = new Headers({ Accept: 'application/json' })

  if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  if (options.authenticated !== false) {
    const token = getAccessToken()
    if (token) headers.set('Authorization', `Bearer ${token}`)
  }

  return fetch(`${API_BASE_URL}${path}`, {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: 'include',
    signal: options.signal ?? null,
  })
}

let refreshInFlight: Promise<string> | null = null

async function doRefresh(): Promise<string> {
  const response = await send('/api/auth/refresh', {
    method: 'POST',
    body: { refreshToken: getRefreshToken() },
    authenticated: false,
  })

  if (!response.ok) {
    clearTokens()
    throw new ApiError(response.status, await readProblem(response), 'Session expired')
  }

  const pair = await readJson<TokenPair>(response)
  setTokens(pair)
  return pair.accessToken
}

export function refreshAccessToken(): Promise<string> {
  refreshInFlight ??= doRefresh().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

export function __resetRefreshInFlight(): void {
  refreshInFlight = null
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options)

  if (response.status === 401 && options.authenticated !== false) {
    await refreshAccessToken()
    response = await send(path, options)
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response), response.statusText)
  }

  return readJson<T>(response)
}
