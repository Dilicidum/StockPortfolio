import { useTranslation } from 'react-i18next'
import { Button } from '../components/Button'
import type { SaveState } from '../lib/useSaveState'

export interface SaveButtonProps {
  state: SaveState
  onClick: () => void
  disabled?: boolean
  label?: string
}

export function SaveButton({ state, onClick, disabled = false, label }: SaveButtonProps) {
  const { t } = useTranslation('common')

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Button
        size="sm"
        className="sm:max-w-[140px]"
        onClick={onClick}
        disabled={disabled || state === 'saving'}
        loading={state === 'saving'}
      >
        {state === 'saving' ? t('actions.saving') : (label ?? t('actions.save'))}
      </Button>
      {state === 'saved' ? (
        <span role="status" className="text-up text-[12.5px]">
          {t('actions.saved')}
        </span>
      ) : null}
    </div>
  )
}
