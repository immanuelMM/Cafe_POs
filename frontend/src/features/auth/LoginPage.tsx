import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { login } from './authApi'
import { useAuthStore } from './authStore'
import { useBrandName } from '../branding/brandingApi'
import { BrandLogo } from '../branding/BrandLogo'
import { ApiError, errorMessage } from '../../lib/api'
import { Field } from '../../components/ui'

const schema = z.object({
  email: z.string().min(1, 'Email is required').email('Enter a valid email'),
  password: z.string().min(1, 'Password is required'),
})
type Values = z.infer<typeof schema>

/** Shape of the `locations` extension on the 400 "Location required" login response. */
interface LocChoice {
  id: string
  name: string
}

export function LoginPage() {
  const user = useAuthStore((s) => s.user)
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const [choices, setChoices] = useState<LocChoice[] | null>(null)
  const [locationId, setLocationId] = useState('')
  const brandName = useBrandName()
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema) })

  if (user) return <Navigate to={(location.state as { from?: string } | null)?.from ?? '/pos'} replace />

  const onSubmit = async (v: Values) => {
    setError(null)
    if (choices && !locationId) {
      setError('Choose a location to sign in to.')
      return
    }
    try {
      await login(v.email, v.password, locationId || null)
      navigate((location.state as { from?: string } | null)?.from ?? '/pos', { replace: true })
    } catch (e) {
      if (e instanceof ApiError && e.status === 400 && Array.isArray(e.extensions.locations)) {
        setChoices(e.extensions.locations as LocChoice[])
        setError('Choose a location to sign in to.')
      } else if (e instanceof ApiError && e.status === 423) {
        setError('This account is temporarily locked after too many failed attempts. Try again in a few minutes.')
      } else if (e instanceof ApiError && e.status === 401) {
        setError('Incorrect email or password.')
      } else {
        setError(errorMessage(e))
      }
    }
  }

  return (
    <div className="login-wrap">
      <form className="login-card" onSubmit={handleSubmit(onSubmit)} noValidate>
        <div className="login-logo">
          <BrandLogo className="login-logo-img" />
        </div>
        <h1>{brandName} POS</h1>
        <p className="muted">Sign in to start taking orders.</p>
        {error && (
          <div className="alert alert-error" role="alert">
            {error}
          </div>
        )}
        <Field label="Email" error={errors.email?.message}>
          <input type="email" autoComplete="username" inputMode="email" {...register('email')} />
        </Field>
        <Field label="Password" error={errors.password?.message}>
          <input type="password" autoComplete="current-password" {...register('password')} />
        </Field>
        {choices && (
          <Field label="Location">
            <select value={locationId} onChange={(e) => setLocationId(e.target.value)} required>
              <option value="">Select…</option>
              {choices.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </Field>
        )}
        <button className="btn btn-primary btn-block" type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
