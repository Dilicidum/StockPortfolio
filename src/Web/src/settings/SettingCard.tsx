import type { ReactNode } from 'react'
import { Alert } from '../components/Alert'
import { Card } from '../components/Card'
import type { SaveStateApi } from '../lib/useSaveState'
import { SaveButton } from './SaveButton'

export interface SettingCardProps {
  title: ReactNode
  save: SaveStateApi
  onSave: () => void
  children: ReactNode
}

export function SettingCard({ title, save, onSave, children }: SettingCardProps) {
  return (
    <Card title={title}>
      <div className="flex flex-col gap-3">
        {children}
        <SaveButton state={save.state} onClick={onSave} />
        {save.state === 'error' ? <Alert tone="error">{save.error}</Alert> : null}
      </div>
    </Card>
  )
}
