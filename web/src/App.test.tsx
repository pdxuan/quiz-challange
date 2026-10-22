import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import App from './App'
import { fakeFetch } from './api/fakeFetch'
import { FakeQuizHub } from './realtime/fakeHub'
import { HubFactoryContext } from './realtime/hub'

function renderAt(path: string) {
  vi.stubGlobal(
    'fetch',
    fakeFetch({
      'GET /api/auth/me': { status: 200, body: { userId: 'u1', username: 'alice' } },
      'GET /api/quiz-sets': { status: 200, body: [] },
    }).fetch,
  )
  const hub = new FakeQuizHub()
  hub.joinReply = () => new Promise(() => {})
  hub.watchReply = () => new Promise(() => {})
  return render(
    <HubFactoryContext value={() => hub}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </HubFactoryContext>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('App', () => {
  it('renders the app title on every route', () => {
    renderAt('/')

    expect(screen.getByRole('heading', { level: 1, name: 'Vocabulary Quiz' })).toBeTruthy()
  })

  it.each([
    ['/login', 'Log in'],
    ['/', 'Join a quiz'],
    ['/host', 'Host a quiz'],
    ['/host/ABC234', 'Hosting ABC234'],
    ['/play/ABC234', 'Playing ABC234'],
  ])('routes %s to its view', async (path, heading) => {
    renderAt(path)

    expect(await screen.findByRole('heading', { level: 2, name: heading })).toBeTruthy()
  })
})
