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

/**
 * These rules mirror the server's `RegisterRequestValidator`. The client copy
 * exists to save a round trip, not to be the authority — the server validates
 * again regardless, and if the two ever disagree the server's 400 wins and
 * lands under the right field.
 *
 * `confirmPassword` is client-only. The API contract takes {email, password};
 * a confirmation field is a UI affordance and is stripped before submit.
 */
const schema = z
  .object({
    email: z.email('Enter a valid email address.'),
    password: z
      .string()
      .min(8, 'Use at least 8 characters.')
      .regex(/[A-Za-z]/, 'Include at least one letter.')
      .regex(/[0-9]/, 'Include at least one digit.'),
    confirmPassword: z.string().min(1, 'Repeat your password.'),
  })
  .refine((values) => values.password === values.confirmPassword, {
    path: ['confirmPassword'],
    message: 'Passwords do not match.',
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
            autoComplete="new-password"
            placeholder="••••••••"
            hint="At least 8 characters, with a letter and a digit."
            error={errors.password?.message}
            {...register('password')}
          />
          <TextField
            label="Confirm password"
            type="password"
            autoComplete="new-password"
            placeholder="••••••••"
            error={errors.confirmPassword?.message}
            {...register('confirmPassword')}
          />
        </div>

        <Button type="submit" size="lg" loading={isSubmitting}>
          Create account
        </Button>
      </form>
    </AuthLayout>
  )
}
