import type { ReactNode } from 'react'

type Tone = 'neutral' | 'up' | 'down'

const tones: Record<Tone, string> = {
  neutral: 'text-tx',
  up: 'text-up',
  down: 'text-dn',
}

export interface StatTileProps {
  label: string
  value: ReactNode
  hint?: ReactNode
  tone?: Tone
}

export function StatTile({ label, value, hint, tone = 'neutral' }: StatTileProps) {
  return (
    <div className="border-bd bg-panel flex flex-col gap-2 rounded-xl border px-[18px] py-4">
      <span className="text-mu text-[11.5px] tracking-[0.04em] uppercase">{label}</span>
      <span className={`font-mono text-2xl font-semibold tracking-[-0.02em] ${tones[tone]}`}>{value}</span>
      {hint ? <span className={`font-mono text-xs ${tone === 'neutral' ? 'text-mu' : tones[tone]}`}>{hint}</span> : null}
    </div>
  )
}
