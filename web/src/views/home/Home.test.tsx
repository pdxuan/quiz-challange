import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import App from '../../App'
import { fakeFetch, type FakeRoutes } from '../../api/fakeFetch'
import { FakeQuizHub } from '../../realtime/fakeHub'
import { HubFactoryContext } from '../../realtime/hub'

const me: FakeRoutes = { 'GET /api/auth/me': { status: 200, body: { userId: 'u1', username: 'alice' } } }

function renderApp(path: string, routes: FakeRoutes) {
  const fake = fakeFetch(routes)
  vi.stubGlobal('fetch', fake.fetch)
  const hub = new FakeQuizHub()
  hub.joinReply = () => new Promise(() => {})
  render(
    <HubFactoryContext value={() => hub}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </HubFactoryContext>,
  )
  return { ...fake, hub }
}

afterEach(() => vi.unstubAllGlobals())

describe('Home', () => {
  it('joins by quiz ID (case-insensitive) and opens the player view', async () => {
    const { hub } = renderApp('/', me)
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Quiz ID'), ' abc234 ')
    await user.click(screen.getByRole('button', { name: 'Join' }))

    expect(await screen.findByRole('heading', { level: 2, name: 'Playing ABC234' })).toBeTruthy()
    expect(hub.invoked('JoinQuiz')[0]?.args).toEqual(['ABC234'])
  })

  it('logs out and returns to the login form', async () => {
    const { calls } = renderApp('/', { ...me, 'POST /api/auth/logout': { status: 204 } })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Log out' }))

    expect(await screen.findByRole('heading', { level: 2, name: 'Log in' })).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/auth/logout')).toBe(true)
  })
})
