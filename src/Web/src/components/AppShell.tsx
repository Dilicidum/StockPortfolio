import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useAuth } from '../auth/useAuth'
import { Button } from './Button'
import { Logo } from './Logo'

interface NavItem {
  to: string
  label: string
}

/**
 * Alerts joins this list in phase 4.
 *
 * An earlier comment here claimed a nav entry pointing at an unknown route is a
 * type error. It is not, and relying on that would be a silent 404: `NavItem.to`
 * is declared `string`, and TanStack Router's `ToPathOption` short-circuits on
 * `string extends TTo ? string : …`, which switches the literal check off
 * entirely. Only an inline literal `to="/somewhere"` on a `<Link>` is checked.
 * Every entry below must be verified against `routeTree.gen.ts` by hand.
 */
const NAV: NavItem[] = [
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/portfolio', label: 'Portfolio' },
]

function initialsOf(email: string): string {
  const local = email.split('@')[0] ?? ''
  const letters = local.replace(/[^a-zA-Z0-9]/g, '')
  return (letters.slice(0, 2) || 'TZ').toUpperCase()
}

export interface AppShellProps {
  title: string
  subtitle?: string
  children: ReactNode
}

/**
 * Two columns above `lg`, stacked below it. At 375px the sidebar becomes a
 * horizontal strip: brand and account on one row, nav scrolling underneath.
 * Nothing is hidden behind a hamburger, because with one nav item a drawer
 * would be pure ceremony.
 */
export function AppShell({ title, subtitle, children }: AppShellProps) {
  const { user, logout, isSigningOut } = useAuth()
  const email = user?.email ?? ''

  return (
    <div className="min-h-screen bg-bg text-tx lg:grid lg:grid-cols-[minmax(0,232px)_minmax(0,1fr)]">
      <aside className="flex flex-col gap-5 border-b border-bd bg-panel px-4 py-4 lg:gap-6 lg:border-b-0 lg:border-r lg:px-3.5 lg:py-5">
        <div className="flex items-center justify-between px-2">
          <Logo />
          <div className="lg:hidden">
            <Button variant="ghost" size="sm" onClick={() => void logout()} loading={isSigningOut}>
              Sign out
            </Button>
          </div>
        </div>

        <nav aria-label="Main" className="-mx-1 flex gap-1 overflow-x-auto px-1 lg:mx-0 lg:flex-col lg:gap-0.5 lg:overflow-visible lg:px-0">
          {NAV.map((item) => (
            <Link
              key={item.to}
              to={item.to}
              className="flex shrink-0 items-center gap-2.5 rounded-lg px-2.5 py-2.5 text-[13.5px] font-medium text-mu transition-colors hover:text-tx"
              activeProps={{ className: 'bg-ac-soft text-ac hover:text-ac' }}
            >
              {({ isActive }) => (
                <>
                  <span
                    aria-hidden="true"
                    className={`h-1.5 w-1.5 rounded-full ${isActive ? 'bg-ac' : 'bg-bd'}`}
                  />
                  {item.label}
                </>
              )}
            </Link>
          ))}
        </nav>

        <div className="mt-auto hidden lg:block">
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start"
            onClick={() => void logout()}
            loading={isSigningOut}
          >
            Sign out
          </Button>
        </div>
      </aside>

      <main className="flex min-w-0 flex-col">
        <header className="flex flex-wrap items-center justify-between gap-4 border-b border-bd bg-panel px-5 py-4 lg:px-7">
          <div className="flex flex-col gap-0.5">
            <h1 className="text-lg font-semibold tracking-[-0.015em]">{title}</h1>
            {subtitle ? <span className="text-mu text-xs">{subtitle}</span> : null}
          </div>

          <div className="flex items-center gap-2.5 rounded-full border border-bd bg-panel-2 py-[5px] pr-3 pl-[6px]">
            <span
              aria-hidden="true"
              className="grid h-6 w-6 place-items-center rounded-full bg-ac-soft text-[11px] font-semibold text-ac"
            >
              {initialsOf(email)}
            </span>
            <span className="max-w-[42vw] truncate text-[12.5px]" title={email}>
              {email}
            </span>
          </div>
        </header>

        <div className="flex flex-col gap-5 px-5 pt-6 pb-10 lg:px-7">{children}</div>
      </main>
    </div>
  )
}
