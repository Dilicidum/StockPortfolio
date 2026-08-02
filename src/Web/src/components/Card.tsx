import type { ReactNode } from 'react'

export interface CardProps {
  title?: ReactNode
  action?: ReactNode
  children?: ReactNode
  className?: string
  /** Drop the inner padding when the body is a full-bleed table or list. */
  bleed?: boolean
}

export function Card({ title, action, children, className = '', bleed = false }: CardProps) {
  return (
    <section className={`rounded-xl border border-bd bg-panel ${className}`}>
      {title || action ? (
        <header className="flex items-center justify-between gap-3 border-b border-bd px-[18px] py-[14px]">
          <h2 className="text-sm font-semibold">{title}</h2>
          {action}
        </header>
      ) : null}
      <div className={bleed ? '' : 'p-[18px]'}>{children}</div>
    </section>
  )
}
