import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Logo } from './Logo'

export interface AuthLayoutProps {
  mode: 'login' | 'register'
  redirectTo?: string | undefined
  children: ReactNode
}

export function AuthLayout({ mode, redirectTo, children }: AuthLayoutProps) {
  const { t } = useTranslation('auth')
  const tabClass =
    'flex-1 rounded-[7px] py-[9px] text-center text-[13px] font-medium transition-colors'
  const activeTab = 'bg-panel text-tx shadow-sm'
  const idleTab = 'text-mu hover:text-tx'
  const search = redirectTo ? { redirect: redirectTo } : {}

  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-8 bg-bg px-5 py-10 text-tx">
      <Logo size={22} />

      <div className="flex w-full max-w-[344px] flex-col gap-5">
        <nav
          aria-label={t('tabs.listLabel')}
          className="flex gap-1 rounded-[10px] border border-bd bg-panel-2 p-1"
        >
          <Link
            to="/login"
            search={search}
            aria-current={mode === 'login' ? 'page' : undefined}
            className={`${tabClass} ${mode === 'login' ? activeTab : idleTab}`}
          >
            {t('tabs.signIn')}
          </Link>
          <Link
            to="/register"
            search={search}
            aria-current={mode === 'register' ? 'page' : undefined}
            className={`${tabClass} ${mode === 'register' ? activeTab : idleTab}`}
          >
            {t('tabs.createAccount')}
          </Link>
        </nav>

        {children}
      </div>
    </div>
  )
}
