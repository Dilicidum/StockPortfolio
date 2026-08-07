import { useId, type ReactNode } from 'react'

export interface FieldControl {
  id: string
  'aria-invalid': true | undefined
  'aria-describedby': string | undefined
}

export interface FieldProps {
  label: string
  error?: string | undefined
  hint?: string | undefined
  children: (control: FieldControl) => ReactNode
}

export function controlClass(error: string | undefined, extra = ''): string {
  return (
    'rounded-[9px] border bg-panel px-[13px] py-[11px] text-sm text-tx ' +
    'placeholder:text-mu/70 focus-visible:outline-2 focus-visible:outline-offset-0 ' +
    'focus-visible:outline-ac transition-colors ' +
    (error ? 'border-dn ' : 'border-bd ') +
    extra
  )
}

export function Field({ label, error, hint, children }: FieldProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`
  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ')

  return (
    <div className="flex flex-col gap-[7px]">
      <label htmlFor={id} className="text-mu text-xs">
        {label}
      </label>
      {children({
        id,
        'aria-invalid': error ? true : undefined,
        'aria-describedby': describedBy === '' ? undefined : describedBy,
      })}
      {hint ? (
        <span id={hintId} className="text-mu text-[11.5px]">
          {hint}
        </span>
      ) : null}
      {error ? (
        <span id={errorId} role="alert" className="text-dn text-[11.5px]">
          {error}
        </span>
      ) : null}
    </div>
  )
}
