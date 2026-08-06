import { useState } from 'react'
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Alert } from '../components/Alert'
import { AuthLayout } from '../components/AuthLayout'
import { Button } from '../components/Button'
import { TextField } from '../components/TextField'
import { useAuth } from '../auth/useAuth'
import { applyServerErrors, translateFieldError } from '../lib/formErrors'
import { safeRedirect } from '../lib/safeRedirect'

const schema = z.object({
  email: z.email('errors.email.format'),
  password: z.string().min(1, 'errors.password.required'),
})

type LoginForm = z.infer<typeof schema>

export const Route = createFileRoute('/login')({
  validateSearch: (search: Record<string, unknown>): { redirect?: string } =>
    typeof search['redirect'] === 'string' ? { redirect: search['redirect'] } : {},
  beforeLoad: ({ context, search }) => {
    if (context.auth.getState().isAuthenticated) {
      throw redirect({ to: safeRedirect(search.redirect) })
    }
  },
  component: LoginPage,
})

function LoginPage() {
  const { redirect: redirectParam } = Route.useSearch()
  const { login } = useAuth()
  const router = useRouter()
  const { t } = useTranslation('auth')
  const [formError, setFormError] = useState('')

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setFormError('')
    try {
      await login(values)
      await router.navigate({ to: safeRedirect(redirectParam) })
    } catch (error) {
      setFormError(applyServerErrors(error, setError, ['email', 'password']))
    }
  })

  return (
    <AuthLayout mode="login" redirectTo={redirectParam}>
      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        {formError ? <Alert tone="error">{formError}</Alert> : null}

        <div className="flex flex-col gap-3">
          <TextField
            label={t('fields.emailLabel')}
            type="email"
            autoComplete="email"
            placeholder={t('fields.emailPlaceholder')}
            error={translateFieldError(t, errors.email?.message)}
            {...register('email')}
          />
          <TextField
            label={t('fields.passwordLabel')}
            type="password"
            autoComplete="current-password"
            placeholder={t('fields.passwordPlaceholder')}
            error={translateFieldError(t, errors.password?.message)}
            {...register('password')}
          />
        </div>

        <Button type="submit" size="lg" loading={isSubmitting}>
          {t('login.submit')}
        </Button>

        <p className="text-mu text-xs leading-relaxed">{t('login.sessionNote')}</p>
      </form>
    </AuthLayout>
  )
}
