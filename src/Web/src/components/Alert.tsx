import type { ReactNode } from 'react'

type Tone = 'error' | 'info' | 'success' | 'warn'

const tones: Record<Tone, string> = {
  error: 'border-dn/45 bg-dn/10 text-dn',
  info: 'border-bd bg-panel-2 text-mu',
  success: 'border-up/45 bg-up/10 text-up',
  warn: 'border-warn/45 bg-warn/10 text-warn',
}

export interface AlertProps {
  tone?: Tone
  title?: ReactNode
  children?: ReactNode
}

export function Alert({ tone = 'error', title, children }: AlertProps) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={`flex flex-col gap-1 rounded-[10px] border px-[13px] py-[11px] text-[12.5px] leading-relaxed ${tones[tone]}`}
    >
      {title ? <strong className="font-semibold">{title}</strong> : null}
      {children}
    </div>
  )
}
