import { useState } from 'react'
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Alert } from '../components/Alert'
import { AuthLayout } from '../components/AuthLayout'
import { Button } from '../components/Button'
import { TextField } from '../components/TextField'
import { useAuth } from '../auth/useAuth'
import { applyServerErrors } from '../lib/formErrors'
import { safeRedirect } from '../lib/safeRedirect'

const schema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
})

type LoginForm = z.infer<typeof schema>

/**
 * `validateSearch` is hand-written rather than a zod resolver so the parse can
 * never throw on a malformed URL — a bad `?redirect=` should degrade to the
 * dashboard, not render a router error boundary over the login form.
 */
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
      // A 401 is an expected outcome of this form, not a crash. It lands in the
      // banner; a 400's field errors land under their fields.
      setFormError(applyServerErrors(error, setError, ['email', 'password']))
    }
  })

  return (
    <AuthLayout mode="login" redirectTo={redirectParam}>
      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        {formError ? <Alert tone="error">{formError}</Alert> : null}

        <div className="flex flex-col gap-3">
          <TextField
            label="Email"
            type="email"
            autoComplete="email"
            placeholder="you@example.com"
            error={errors.email?.message}
            {...register('email')}
          />
          <TextField
            label="Password"
            type="password"
            autoComplete="current-password"
            placeholder="••••••••"
            error={errors.password?.message}
            {...register('password')}
          />
        </div>

        <Button type="submit" size="lg" loading={isSubmitting}>
          Sign in
        </Button>

        <p className="text-mu text-xs leading-relaxed">
          One session for this browser, shared by every tab. Signing out ends it
          everywhere. On a shared computer, sign out rather than just closing
          the window.
        </p>
      </form>
    </AuthLayout>
  )
}
