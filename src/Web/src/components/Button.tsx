import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { Spinner } from './Spinner'

/*
 * Hand-built, on purpose. The brief bans UI kits — no Radix, no Headless UI,
 * no React Aria. A <button> already has the keyboard behaviour, the focus ring
 * and the disabled semantics; all it needs is Tailwind.
 */

type Variant = 'primary' | 'secondary' | 'ghost'

const base =
  'inline-flex items-center justify-center gap-2 rounded-[9px] font-medium ' +
  'transition-colors duration-150 disabled:cursor-not-allowed disabled:opacity-55 ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ac'

const variants: Record<Variant, string> = {
  primary: 'bg-ac text-ac-contrast font-semibold hover:opacity-90',
  secondary: 'border border-bd bg-panel-2 text-tx hover:bg-panel',
  ghost: 'text-mu hover:text-tx',
}

const sizes = {
  sm: 'px-3 py-1.5 text-xs',
  md: 'px-4 py-2.5 text-sm',
  lg: 'w-full px-4 py-3 text-sm',
} as const

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: keyof typeof sizes
  loading?: boolean
  children?: ReactNode
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  disabled,
  className = '',
  children,
  ...rest
}: ButtonProps) {
  return (
    <button
      type="button"
      {...rest}
      disabled={disabled === true || loading}
      // aria-busy rather than swapping the label for a spinner: screen-reader
      // users keep the accessible name while the request is in flight.
      aria-busy={loading || undefined}
      className={`${base} ${variants[variant]} ${sizes[size]} ${className}`}
    >
      {loading ? <Spinner size={14} /> : null}
      {children}
    </button>
  )
}
