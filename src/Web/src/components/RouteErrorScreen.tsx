import { useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Alert } from './Alert'
import { Button } from './Button'

export function RouteErrorScreen({ error }: ErrorComponentProps) {
  const router = useRouter()
  const { t } = useTranslation('common')

  return (
    <div className="flex min-h-dvh items-center justify-center bg-bg px-6 text-tx">
      <div className="flex w-full max-w-md flex-col gap-4">
        <Alert tone="error" title={t('routeError.title')}>
          {error.message || t('fallbackError')}
        </Alert>

        <Button onClick={() => void router.invalidate()}>{t('actions.tryAgain')}</Button>
      </div>
    </div>
  )
}
