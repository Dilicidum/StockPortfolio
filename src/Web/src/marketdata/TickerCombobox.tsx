import { useId, useRef, useState, type KeyboardEvent, type Ref } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useDebouncedValue } from '../lib/useDebouncedValue'
import { MIN_SEARCH_LENGTH, tickerSearchQuery, type TickerSuggestion } from './tickerSearchApi'

export interface TickerComboboxProps {
  label: string
  value: string
  onChange: (value: string) => void
  onBlur?: (() => void) | undefined
  /** react-hook-form's own ref, forwarded to the input so `setFocus` and `ref` still work. */
  inputRef?: Ref<HTMLInputElement> | undefined
  error?: string | undefined
  placeholder?: string | undefined
}

/**
 * Long enough that typing a four-letter symbol costs one request rather than four,
 * short enough that the list appears while the finger is still on the keyboard.
 */
const DEBOUNCE_MS = 250

/**
 * A hand-built combobox — no Radix, no Headless UI, no React Aria. The brief bans UI
 * component libraries and its list ends in "тощо", so this is the field itself: an
 * `<input role="combobox">` owning a `<ul role="listbox">` through `aria-controls`, with
 * `aria-activedescendant` moving the selection while DOM focus stays in the input. That
 * last part is the whole reason the pattern exists — focus never leaves the text box, so
 * typing and choosing are the same gesture.
 *
 * IT IS A TEXT BOX FIRST. Nothing here can make a value un-typeable: the list is a
 * convenience, Enter with nothing highlighted submits the form, and an empty result set —
 * whether because nothing matched or because search is down, which look identical by
 * design — renders no popup at all and leaves a plain field behind.
 */
export function TickerCombobox({
  label,
  value,
  onChange,
  onBlur,
  inputRef,
  error,
  placeholder,
}: TickerComboboxProps) {
  const id = useId()
  const listboxId = `${id}-listbox`
  const errorId = `${id}-error`
  const optionId = (index: number) => `${id}-option-${index}`

  // `open` is "the user is working in this field", not "the popup is visible" — the popup
  // also needs matches. It gates the query, so leaving the field stops searching for it.
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)

  const localRef = useRef<HTMLInputElement | null>(null)

  const debounced = useDebouncedValue(value.trim(), DEBOUNCE_MS)
  const enabled = open && debounced.length >= MIN_SEARCH_LENGTH

  const { data } = useQuery({ ...tickerSearchQuery(debounced), enabled })

  // Gated on `enabled` as well as on the data, because the answer stays in the cache after
  // the field is left. Without this, re-focusing a filled-in field pops a list back open.
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
    // Focus stayed in the input for a keyboard pick; a mouse pick would otherwise leave it
    // on the option's dead <li>, so put it back either way.
    localRef.current?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()

      // Re-opens a field that was closed with Escape, without needing a keystroke that
      // changes the text.
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
      // Only swallowed when something is highlighted. With nothing highlighted this is a
      // plain text box and Enter submits the form, which is what typing a known symbol
      // and pressing Enter has always done.
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
          // The browser's own suggestion list would sit on top of this one.
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
            // A new term makes the old highlight meaningless, and leaving it would let
            // Enter pick a row the user can no longer see.
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
            aria-label={`${label} suggestions`}
            className="border-bd bg-panel absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-[9px] border py-1 shadow-lg"
          >
            {suggestions.map((suggestion, index) => (
              <li
                key={suggestion.symbol}
                id={optionId(index)}
                role="option"
                aria-selected={index === activeIndex}
                // Stops the input from blurring, which would close the list before the
                // click ever landed on it.
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

      {/*
       * Deliberately NOT role="status". The pages this field sits on already use `Alert`
       * for their one polite live region, and a second element with that role would make
       * every `getByRole('status')` ambiguous. `aria-live` alone announces the count and
       * carries no role at all.
       */}
      <div aria-live="polite" className="sr-only">
        {expanded ? `${suggestions.length} ${suggestions.length === 1 ? 'match' : 'matches'}` : ''}
      </div>

      {error ? (
        <span id={errorId} role="alert" className="text-dn text-[11.5px]">
          {error}
        </span>
      ) : null}
    </div>
  )
}
