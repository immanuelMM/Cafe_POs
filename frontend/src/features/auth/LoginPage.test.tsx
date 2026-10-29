import { http, HttpResponse } from 'msw'
import { Route, Routes } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { makeAuth, resetStores, url } from '../../test/fixtures'
import { useAuthStore } from './authStore'
import { LoginPage } from './LoginPage'

const tree = (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/pos" element={<p>pos page</p>} />
  </Routes>
)

// The login page asks for public branding; answer with an empty object so it falls back to defaults.
const brandingHandler = http.get(url('/api/v1/branding'), () => HttpResponse.json({}))

async function fill(email: string, password: string) {
  const user = userEvent.setup()
  if (email) await user.type(screen.getByLabelText(/^Email/), email)
  if (password) await user.type(screen.getByLabelText(/^Password/), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
  return user
}

describe('LoginPage', () => {
  beforeEach(() => {
    resetStores()
    server.use(brandingHandler)
  })

  it('validates required fields and email format without calling the API', async () => {
    let called = false
    server.use(
      http.post(url('/api/v1/auth/login'), () => {
        called = true
        return HttpResponse.json(makeAuth())
      }),
    )
    renderWithProviders(tree, { route: '/login' })
    await fill('', '')
    expect(await screen.findByText('Email is required')).toBeInTheDocument()
    expect(screen.getByText('Password is required')).toBeInTheDocument()

    await fill('not-an-email', 'x')
    expect(await screen.findByText('Enter a valid email')).toBeInTheDocument()
    expect(called).toBe(false)
  })

  it('signs in, stores the session in memory and navigates to /pos', async () => {
    let body: unknown
    server.use(
      http.post(url('/api/v1/auth/login'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makeAuth())
      }),
    )
    renderWithProviders(tree, { route: '/login' })
    await fill('cashier@cafe.local', 'Secret#123')
    expect(await screen.findByText('pos page')).toBeInTheDocument()
    expect(body).toEqual({ email: 'cashier@cafe.local', password: 'Secret#123', locationId: null })
    expect(useAuthStore.getState().accessToken).toBe('token-1')
  })

  it('shows an error for wrong credentials (401) and stays on the page', async () => {
    server.use(
      http.post(url('/api/v1/auth/login'), () => HttpResponse.json({ title: 'Unauthorized' }, { status: 401 })),
    )
    renderWithProviders(tree, { route: '/login' })
    await fill('a@b.co', 'wrong')
    expect(await screen.findByRole('alert')).toHaveTextContent('Incorrect email or password.')
    expect(useAuthStore.getState().user).toBeNull()
  })

  it('explains a locked account (423)', async () => {
    server.use(http.post(url('/api/v1/auth/login'), () => HttpResponse.json({ title: 'Locked' }, { status: 423 })))
    renderWithProviders(tree, { route: '/login' })
    await fill('a@b.co', 'wrong')
    expect(await screen.findByRole('alert')).toHaveTextContent(/temporarily locked/i)
  })

  it('asks a multi-location user to choose a location, then signs in with it', async () => {
    const bodies: { locationId: string | null }[] = []
    server.use(
      http.post(url('/api/v1/auth/login'), async ({ request }) => {
        const b = (await request.json()) as { locationId: string | null }
        bodies.push(b)
        if (!b.locationId) {
          return HttpResponse.json(
            {
              title: 'Location required',
              locations: [
                { id: 'loc-main', name: 'Main Branch' },
                { id: 'loc-up', name: 'Uptown Branch' },
              ],
            },
            { status: 400 },
          )
        }
        return HttpResponse.json(makeAuth({ role: 'Manager' }))
      }),
    )
    renderWithProviders(tree, { route: '/login' })
    const user = await fill('manager@cafe.local', 'Secret#123')
    expect(await screen.findByRole('alert')).toHaveTextContent('Choose a location to sign in to.')

    // Submitting without choosing keeps the user here and does not hit the API again.
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(bodies).toHaveLength(1)

    await user.selectOptions(screen.getByLabelText('Location'), 'loc-up')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('pos page')).toBeInTheDocument()
    expect(bodies[1].locationId).toBe('loc-up')
  })

  it('shows a network failure message', async () => {
    server.use(http.post(url('/api/v1/auth/login'), () => HttpResponse.error()))
    renderWithProviders(tree, { route: '/login' })
    await fill('a@b.co', 'x')
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/cannot reach the server/i))
  })
})
