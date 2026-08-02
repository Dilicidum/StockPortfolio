/**
 * The `redirect` search param is attacker-supplied: anyone can mail a link to
 * /login?redirect=https://evil.example and the app would happily bounce a
 * freshly authenticated user there. Only same-origin, path-absolute targets are
 * accepted; everything else falls back to the dashboard.
 *
 * "//evil.example" is the one people miss — the browser reads a protocol-
 * relative URL as an absolute one, so a bare `startsWith('/')` check lets it
 * through. Same for a backslash, which some parsers normalise to a slash.
 */
const DEFAULT_TARGET = '/dashboard'

export function safeRedirect(target: string | undefined): string {
  if (!target) return DEFAULT_TARGET
  if (!target.startsWith('/')) return DEFAULT_TARGET
  if (target.startsWith('//') || target.startsWith('/\\')) return DEFAULT_TARGET
  return target
}
