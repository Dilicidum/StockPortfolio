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

/**
 * These rules mirror the server's `RegisterRequestValidator`. The client copy
 * exists to save a round trip, not to be the authority — the server validates
 * again regardless, and if the two ever disagree the server's 400 wins and
 * lands under the right field.
 *
 * `confirmPassword` is client-only. The API contract takes {email, password};
 * a confirmation field is a UI affordance and is stripped before submit.
 *
 * Message KEYS, not sentences — see `login.tsx`'s equivalent comment. This form was the
 * other holdout `portfolio.tsx`'s convention had not yet reached.
 */
const schema = z
  .object({
    email: z.email('errors.email.format'),
    password: z
      .string()
      .min(8, 'errors.password.tooShort')
      .regex(/[A-Za-z]/, 'errors.password.needsLetter')
      .regex(/[0-9]/, 'errors.password.needsDigit'),
    confirmPassword: z.string().min(1, 'errors.confirmPassword.required'),
  })
  .refine((values) => values.password === values.confirmPassword, {
    path: ['confirmPassword'],
    message: 'errors.confirmPassword.mismatch',
  })

type RegisterForm = z.infer<typeof schema>

export const Route = createFileRoute('/register')({
  validateSearch: (search: Record<string, unknown>): { redirect?: string } =>
    typeof search['redirect'] === 'string' ? { redirect: search['redirect'] } : {},
  beforeLoad: ({ context, search }) => {
    if (context.auth.getState().isAuthenticated) {
      throw redirect({ to: safeRedirect(search.redirect) })
    }
  },
  component: RegisterPage,
})

function RegisterPage() {
  const { redirect: redirectParam } = Route.useSearch()
  const { register: signUp } = useAuth()
  const router = useRouter()
  const { t } = useTranslation('auth')
  const [formError, setFormError] = useState('')

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterForm>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '', confirmPassword: '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setFormError('')
    try {
      await signUp({ email: values.email, password: values.password })
      await router.navigate({ to: safeRedirect(redirectParam) })
    } catch (error) {
      // 409 means the email is taken. It has no `errors` object, so it arrives
      // as the banner message — which is where a duplicate-account message
      // belongs anyway, since it is about the account and not the field shape.
      setFormError(applyServerErrors(error, setError, ['email', 'password']))
    }
  })

  return (
    <AuthLayout mode="register" redirectTo={redirectParam}>
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
            autoComplete="new-password"
            placeholder={t('fields.passwordPlaceholder')}
            hint={t('register.passwordHint')}
            error={translateFieldError(t, errors.password?.message)}
            {...register('password')}
          />
          <TextField
            label={t('fields.confirmPasswordLabel')}
            type="password"
            autoComplete="new-password"
            placeholder={t('fields.passwordPlaceholder')}
            error={translateFieldError(t, errors.confirmPassword?.message)}
            {...register('confirmPassword')}
          />
        </div>

        <Button type="submit" size="lg" loading={isSubmitting}>
          {t('register.submit')}
        </Button>
      </form>
    </AuthLayout>
  )
}
