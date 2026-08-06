import { useId, useRef, useState, type KeyboardEvent, type Ref } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useDebouncedValue } from '../lib/useDebouncedValue'
import { MIN_SEARCH_LENGTH, tickerSearchQuery, type TickerSuggestion } from './tickerSearchApi'

export interface TickerComboboxProps {
  label: string
  value: string
  onChange: (value: string) => void
  onBlur?: (() => void) | undefined
  inputRef?: Ref<HTMLInputElement> | undefined
  error?: string | undefined
  placeholder?: string | undefined
}

const DEBOUNCE_MS = 250

export function TickerCombobox({
  label,
  value,
  onChange,
  onBlur,
  inputRef,
  error,
  placeholder,
}: TickerComboboxProps) {
  const { t } = useTranslation('common')
  const id = useId()
  const listboxId = `${id}-listbox`
  const errorId = `${id}-error`
  const optionId = (index: number) => `${id}-option-${index}`

  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)

  const localRef = useRef<HTMLInputElement | null>(null)

  const debounced = useDebouncedValue(value.trim(), DEBOUNCE_MS)
  const enabled = open && debounced.length >= MIN_SEARCH_LENGTH

  const { data } = useQuery({ ...tickerSearchQuery(debounced), enabled })

  const suggestions: TickerSuggestion[] = enabled && data ? data : []
  const expanded = suggestions.length > 0
  const active = activeIndex >= 0 ? suggestions[activeIndex] : undefined

  function setRef(node: HTMLInputElement | null) {
    localRef.current = node

    if (typeof inputRef === 'function') inputRef(node)
    else if (inputRef) inputRef.current = node
  }

  function close() {
    setOpen(false)
    setActiveIndex(-1)
  }

  function select(suggestion: TickerSuggestion) {
    onChange(suggestion.symbol)
    close()
    localRef.current?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()

      if (!open) {
        setOpen(true)
        return
      }

      if (suggestions.length > 0) setActiveIndex((index) => (index + 1) % suggestions.length)
      return
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault()

      if (suggestions.length > 0) {
        setActiveIndex((index) => (index <= 0 ? suggestions.length - 1 : index - 1))
      }
      return
    }

    if (event.key === 'Enter') {
      if (active) {
        event.preventDefault()
        select(active)
      }
      return
    }

    if (event.key === 'Escape' && open) {
      event.preventDefault()
      close()
    }
  }

  return (
    <div className="flex flex-col gap-[7px]">
      <label htmlFor={id} className="text-mu text-xs">
        {label}
      </label>

      <div className="relative">
        <input
          id={id}
          ref={setRef}
          type="text"
          role="combobox"
          value={value}
          placeholder={placeholder}
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          aria-expanded={expanded}
          aria-controls={expanded ? listboxId : undefined}
          aria-autocomplete="list"
          aria-activedescendant={active ? optionId(activeIndex) : undefined}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          onChange={(event) => {
            onChange(event.target.value)
            setOpen(true)
            setActiveIndex(-1)
          }}
          onKeyDown={handleKeyDown}
          onBlur={() => {
            close()
            onBlur?.()
          }}
          className={
            'w-full rounded-[9px] border bg-panel px-[13px] py-[11px] text-sm text-tx ' +
            'placeholder:text-mu/70 focus-visible:outline-2 focus-visible:outline-offset-0 ' +
            'focus-visible:outline-ac transition-colors ' +
            (error ? 'border-dn' : 'border-bd')
          }
        />

        {expanded ? (
          <ul
            id={listboxId}
            role="listbox"
            aria-label={t('search.suggestionsFor', { label })}
            className="border-bd bg-panel absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-[9px] border py-1 shadow-lg"
          >
            {suggestions.map((suggestion, index) => (
              <li
                key={suggestion.symbol}
                id={optionId(index)}
                role="option"
                aria-selected={index === activeIndex}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => select(suggestion)}
                onMouseEnter={() => setActiveIndex(index)}
                className={
                  'flex cursor-pointer items-baseline gap-2 px-[13px] py-2 text-[12.5px] ' +
                  (index === activeIndex ? 'bg-ac-soft' : '')
                }
              >
                <span className="text-tx font-mono">{suggestion.symbol}</span>
                <span className="text-mu truncate">{suggestion.description}</span>
              </li>
            ))}
          </ul>
        ) : null}
      </div>

      <div aria-live="polite" className="sr-only">
        {expanded ? t('search.matchesCount', { count: suggestions.length }) : ''}
      </div>

      {error ? (
        <span id={errorId} role="alert" className="text-dn text-[11.5px]">
          {error}
        </span>
      ) : null}
    </div>
  )
}
