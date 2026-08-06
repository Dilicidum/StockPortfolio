import { forwardRef, type InputHTMLAttributes } from 'react'
import { Field, controlClass } from './Field'

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  error?: string | undefined
  hint?: string | undefined
}

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { label, error, hint, className = '', ...rest },
  ref,
) {
  return (
    <Field label={label} error={error} hint={hint}>
      {(control) => <input {...rest} {...control} ref={ref} className={controlClass(error, className)} />}
    </Field>
  )
})
