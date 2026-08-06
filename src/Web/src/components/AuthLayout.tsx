import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Logo } from './Logo'

export interface AuthLayoutProps {
  mode: 'login' | 'register'
  redirectTo?: string | undefined
  children: ReactNode
}

/**
 * The design mock pairs the form with a marketing hero and a live ticker strip.
 * Both are ornament and both are cut: the hero says nothing a reviewer needs
 * and the ticker strip would have to invent prices for a signed-out visitor.
 * What survives is the part that is actually a control — the sign-in/sign-up
 * segmented switch — built from two <Link>s so each tab is a real, shareable
 * URL that back/forward moves between, not a piece of component state.
 */
export function AuthLayout({ mode, redirectTo, children }: AuthLayoutProps) {
  const { t } = useTranslation('auth')
  const tabClass =
    'flex-1 rounded-[7px] py-[9px] text-center text-[13px] font-medium transition-colors'
  const activeTab = 'bg-panel text-tx shadow-sm'
  const idleTab = 'text-mu hover:text-tx'
  const search = redirectTo ? { redirect: redirectTo } : {}

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-8 bg-bg px-5 py-10 text-tx">
      <Logo size={22} />

      <div className="flex w-full max-w-[344px] flex-col gap-5">
        <div
          role="tablist"
          aria-label={t('tabs.listLabel')}
          className="flex gap-1 rounded-[10px] border border-bd bg-panel-2 p-1"
        >
          <Link
            to="/login"
            search={search}
            role="tab"
            aria-selected={mode === 'login'}
            className={`${tabClass} ${mode === 'login' ? activeTab : idleTab}`}
          >
            {t('tabs.signIn')}
          </Link>
          <Link
            to="/register"
            search={search}
            role="tab"
            aria-selected={mode === 'register'}
            className={`${tabClass} ${mode === 'register' ? activeTab : idleTab}`}
          >
            {t('tabs.createAccount')}
          </Link>
        </div>

        {children}
      </div>
    </div>
  )
}
