import { forwardRef, type SelectHTMLAttributes } from 'react'
import { Field, controlClass } from './Field'

export interface SelectOption {
  value: string | number
  label: string
}

export interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id' | 'children'> {
  label: string
  options: readonly SelectOption[]
  error?: string | undefined
  hint?: string | undefined
}

export const SelectField = forwardRef<HTMLSelectElement, SelectFieldProps>(function SelectField(
  { label, options, error, hint, className = 'sm:max-w-[240px]', ...rest },
  ref,
) {
  return (
    <Field label={label} error={error} hint={hint}>
      {(control) => (
        <select {...rest} {...control} ref={ref} className={controlClass(error, className)}>
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      )}
    </Field>
  )
})
