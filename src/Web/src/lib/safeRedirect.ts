const DEFAULT_TARGET = '/dashboard'

export function safeRedirect(target: string | undefined): string {
  if (!target) return DEFAULT_TARGET
  if (!target.startsWith('/')) return DEFAULT_TARGET
  if (target.startsWith('//') || target.startsWith('/\\')) return DEFAULT_TARGET
  return target
}
