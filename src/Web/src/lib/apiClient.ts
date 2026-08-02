import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setTokens,
  type TokenPair,
} from './tokenStore'

/**
 * Empty string means "same origin", which is the docker-compose case: nginx
 * serves the SPA and proxies /api to the API container, so no CORS at all.
 * GitHub Pages bakes the Azure Container Apps origin in at build time.
 */
export const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? ''

/** RFC 7807. `errors` is the ASP.NET Core ValidationProblemDetails extension. */
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

  /**
   * Field-level errors keyed by camelCase field name. ASP.NET Core emits
   * PascalCase keys ("Email"), react-hook-form knows the field as "email",
   * so normalise here rather than at every call site.
   */
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
  /** Attach the bearer token and refresh-and-retry on 401. Default true. */
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
    // Required for the compose deployment: the refresh token is an httpOnly
    // cookie and will not be sent without it.
    credentials: 'include',
    signal: options.signal ?? null,
  })
}

/**
 * THE DEDUPE. A dashboard fires half a dozen queries at once; when the access
 * token has just expired every one of them comes back 401 within the same tick.
 * Without this, each would POST /api/auth/refresh, the server would rotate the
 * refresh token six times, and five of the six responses would arrive holding a
 * token that has already been superseded — the user is logged out at random,
 * only under load, only sometimes. Classic thundering herd.
 *
 * One in-flight promise, shared by every caller, cleared when it settles.
 * `??=` only evaluates the right-hand side when the slot is empty, and the
 * assignment completes synchronously before `doRefresh` can resolve, so the
 * `.finally` that nulls it can never run before the slot is populated.
 *
 * There is a test that asserts exactly this with an MSW request counter.
 */
let refreshInFlight: Promise<string> | null = null

async function doRefresh(): Promise<string> {
  const response = await send('/api/auth/refresh', {
    method: 'POST',
    // May be null under compose, where the httpOnly cookie carries it instead.
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

/** Test seam. Never call this from application code. */
export function __resetRefreshInFlight(): void {
  refreshInFlight = null
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options)

  // One refresh-and-retry, and only one. If the retry is also rejected the
  // session is genuinely gone and looping would just DoS our own login page.
  if (response.status === 401 && options.authenticated !== false) {
    await refreshAccessToken()
    response = await send(path, options)
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response), response.statusText)
  }

  return readJson<T>(response)
}
