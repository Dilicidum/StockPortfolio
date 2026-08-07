import { useEffect, useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'

export interface ConfirmDialogProps {
  open: boolean
  title: string
  body: string
  confirmLabel: string
  onConfirm: () => void
  onCancel: () => void
  busy?: boolean | undefined
}

export function ConfirmDialog({
  open,
  title,
  body,
  confirmLabel,
  onConfirm,
  onCancel,
  busy,
}: ConfirmDialogProps) {
  const { t } = useTranslation('common')
  const id = useId()
  const titleId = `${id}-title`
  const bodyId = `${id}-body`

  const panelRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const openerRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!open) return

    openerRef.current = document.activeElement as HTMLElement | null
    cancelRef.current?.focus()

    return () => {
      openerRef.current?.focus()
    }
  }, [open])

  useEffect(() => {
    if (!open) return

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCancel()
        return
      }

      if (event.key !== 'Tab') return

      const focusable = panelRef.current?.querySelectorAll<HTMLElement>('button:not([disabled])')
      if (!focusable || focusable.length === 0) return

      const first = focusable[0]!
      const last = focusable[focusable.length - 1]!

      if (!panelRef.current?.contains(document.activeElement)) {
        event.preventDefault()
        first.focus()
        return
      }

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, onCancel])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={onCancel}
      role="presentation"
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
        className="border-bd bg-panel w-full max-w-sm rounded-xl border p-5"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id={titleId} className="text-tx text-[15px] font-semibold">
          {title}
        </h2>
        <p id={bodyId} className="text-mu mt-2 text-[12.5px] leading-relaxed">
          {body}
        </p>

        <div className="mt-5 flex justify-end gap-2">
          <Button ref={cancelRef} type="button" variant="secondary" onClick={onCancel}>
            {t('actions.cancel')}
          </Button>
          <Button type="button" onClick={onConfirm} loading={busy === true}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  )
}
