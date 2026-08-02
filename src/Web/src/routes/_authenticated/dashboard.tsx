import { createFileRoute } from '@tanstack/react-router'
import { AppShell } from '../../components/AppShell'
import { Card } from '../../components/Card'
import { useAuth } from '../../auth/useAuth'

export const Route = createFileRoute('/_authenticated/dashboard')({
  component: DashboardPage,
})

/**
 * Phase 1 ships the shell and nothing else. Totals, holdings and P&L arrive in
 * phase 2 with the Portfolio module behind them; the placeholder says so rather
 * than showing zeroes, because a $0.00 total is indistinguishable from a broken
 * fetch and a reviewer cannot tell which they are looking at.
 */
function DashboardPage() {
  const { user } = useAuth()

  return (
    <AppShell title="Dashboard" subtitle={user ? `Signed in as ${user.email}` : undefined}>
      <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 xl:grid-cols-4">
        {['Total value', 'Invested', 'Unrealised P&L', 'Positions'].map((label) => (
          <div
            key={label}
            className="flex flex-col gap-2 rounded-xl border border-bd bg-panel px-[18px] py-4"
          >
            <span className="text-mu text-[11.5px] tracking-[0.04em] uppercase">{label}</span>
            <span className="font-mono text-2xl font-semibold tracking-[-0.02em] text-mu">—</span>
            <span className="text-mu font-mono text-xs">Phase 2</span>
          </div>
        ))}
      </div>

      <Card title="Holdings">
        <p className="text-mu text-[12.5px] leading-relaxed">
          Nothing here yet. Phase 1 covers sign-up, sign-in, sign-out and session
          persistence; holdings, live quotes and P&amp;L land with the Portfolio
          and MarketData modules.
        </p>
      </Card>
    </AppShell>
  )
}
