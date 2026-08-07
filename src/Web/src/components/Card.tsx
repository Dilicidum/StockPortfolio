import type { ReactNode } from 'react'

export interface CardProps {
  title?: ReactNode
  action?: ReactNode
  children?: ReactNode
}

export function Card({ title, action, children }: CardProps) {
  return (
    <section className="rounded-xl border border-bd bg-panel">
      {title || action ? (
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-bd px-[18px] py-[14px]">
          <h2 className="text-sm font-semibold">{title}</h2>
          {action}
        </header>
      ) : null}
      <div className="p-[18px]">{children}</div>
    </section>
  )
}
