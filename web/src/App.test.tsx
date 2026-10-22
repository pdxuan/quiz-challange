import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import App from './App'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  )
}

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
  ])('routes %s to its view', (path, heading) => {
    renderAt(path)

    expect(screen.getByRole('heading', { level: 2, name: heading })).toBeTruthy()
  })
})
