import { forwardRef, useId, type InputHTMLAttributes } from 'react'

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  error?: string | undefined
  hint?: string | undefined
}

/**
 * `forwardRef` is not optional here: react-hook-form's `register()` returns a
 * ref and hands it to the DOM node. Drop it and the field is uncontrolled,
 * unvalidated, and permanently empty as far as the form is concerned.
 *
 * The error is wired with `aria-describedby` + `aria-invalid` and rendered in
 * `role="alert"`, so a screen reader announces the message rather than leaving
 * a red border as the only signal.
 */
export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { label, error, hint, className = '', ...rest },
  ref,
) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`
  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ')

  return (
    <div className="flex flex-col gap-[7px]">
      <label htmlFor={id} className="text-mu text-xs">
        {label}
      </label>
      <input
        {...rest}
        id={id}
        ref={ref}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy === '' ? undefined : describedBy}
        className={
          'rounded-[9px] border bg-panel px-[13px] py-[11px] text-sm text-tx ' +
          'placeholder:text-mu/70 focus-visible:outline-2 focus-visible:outline-offset-0 ' +
          'focus-visible:outline-ac transition-colors ' +
          (error ? 'border-dn ' : 'border-bd ') +
          className
        }
      />
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
})
