import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import App from '../../App'
import { fakeFetch, type FakeRoutes } from '../../api/fakeFetch'
import { FakeQuizHub } from '../../realtime/fakeHub'
import { HubFactoryContext } from '../../realtime/hub'

function renderApp(path: string, routes: FakeRoutes) {
  const fake = fakeFetch(routes)
  vi.stubGlobal('fetch', fake.fetch)
  const hub = new FakeQuizHub()
  render(
    <HubFactoryContext value={() => hub}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </HubFactoryContext>,
  )
  return fake
}

afterEach(() => vi.unstubAllGlobals())

describe('Login (AC31)', () => {
  it('logs in and navigates to home', async () => {
    let loggedIn = false
    const { calls } = renderApp('/login', {
      'POST /api/auth/login': () => {
        loggedIn = true
        return { status: 200, body: { userId: 'u1', username: 'alice' } }
      },
      'GET /api/auth/me': () => (loggedIn ? { status: 200, body: { userId: 'u1', username: 'alice' } } : { status: 401 }),
    })
    const user = userEvent.setup()

    await user.type(screen.getByLabelText('Username'), 'alice')
    await user.type(screen.getByLabelText('Password'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByRole('heading', { level: 2, name: 'Join a quiz' })).toBeTruthy()
    expect(screen.getByText(/Logged in as alice/)).toBeTruthy()
    expect(calls.find((c) => c.path === '/api/auth/login')?.body).toEqual({ username: 'alice', password: 'secret' })
  })

  it('shows "Wrong username or password" on a 401 and stays on the form', async () => {
    renderApp('/login', {
      'POST /api/auth/login': { status: 401, body: { title: 'Wrong username or password', status: 401 } },
    })
    const user = userEvent.setup()

    await user.type(screen.getByLabelText('Username'), 'alice')
    await user.type(screen.getByLabelText('Password'), 'nope')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect((await screen.findByRole('alert')).textContent).toBe('Wrong username or password')
    expect(screen.getByRole('heading', { level: 2, name: 'Log in' })).toBeTruthy()
  })

  it('redirects home to the login form when not logged in', async () => {
    renderApp('/', { 'GET /api/auth/me': { status: 401 } })

    expect(await screen.findByRole('heading', { level: 2, name: 'Log in' })).toBeTruthy()
  })
})
