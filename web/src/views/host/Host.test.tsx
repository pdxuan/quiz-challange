import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import App from '../../App'
import { fakeFetch, type FakeRoutes } from '../../api/fakeFetch'
import { FakeQuizHub, hubError } from '../../realtime/fakeHub'
import { HubFactoryContext } from '../../realtime/hub'
import type { BoardEntry, QuestionView, WatchSnapshot } from '../../realtime/contracts'

const me: FakeRoutes = { 'GET /api/auth/me': { status: 200, body: { userId: 'u0', username: 'olivia' } } }

function renderApp(path: string, routes: FakeRoutes, hub = new FakeQuizHub()) {
  const fake = fakeFetch({ ...me, ...routes })
  vi.stubGlobal('fetch', fake.fetch)
  render(
    <HubFactoryContext value={() => hub}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </HubFactoryContext>,
  )
  return { ...fake, hub }
}

function snapshot(overrides: Partial<WatchSnapshot> = {}): WatchSnapshot {
  return { state: 'Lobby', players: [], currentQuestion: null, answersCount: null, leaderboard: [], ...overrides }
}

const question: QuestionView = {
  questionId: 'q1',
  index: 2,
  total: 10,
  prompt: 'chien',
  options: [
    { optionId: 'o1', text: 'cat' },
    { optionId: 'o2', text: 'dog' },
    { optionId: 'o3', text: 'bird' },
    { optionId: 'o4', text: 'fish' },
  ],
  endsAt: '2001-01-01T10:00:15.000Z',
  serverNow: '2001-01-01T10:00:00.000Z',
}

function board(count: number): BoardEntry[] {
  return Array.from({ length: count }, (_, i) => ({ rank: i + 1, username: `p${i + 1}`, totalPoints: 2000 - i * 100, online: i !== 11 }))
}

function watching(snap: WatchSnapshot) {
  const hub = new FakeQuizHub()
  hub.watchReply = () => Promise.resolve(snap)
  return hub
}

afterEach(() => vi.unstubAllGlobals())

describe('Organizer setup', () => {
  it('lists quiz sets, opens a session and shows its quiz ID large', async () => {
    const { calls, hub } = renderApp(
      '/host',
      {
        'GET /api/quiz-sets': {
          status: 200,
          body: [
            { id: 'animals', title: 'Animals', questionCount: 10, timeLimitSec: 15 },
            { id: 'food', title: 'Food', questionCount: 8, timeLimitSec: 20 },
          ],
        },
        'POST /api/sessions': { status: 201, body: { quizId: 'XYZ789' } },
      },
      watching(snapshot()),
    )
    const user = userEvent.setup()

    const food = (await screen.findByText('Food')).closest('li')!
    expect(food.textContent).toContain('8 questions, 20 s each')
    await user.click(within(food).getByRole('button', { name: 'Open session' }))

    expect(await screen.findByRole('heading', { level: 2, name: 'Hosting XYZ789' })).toBeTruthy()
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/sessions')?.body).toEqual({ quizSetId: 'food' })
    expect(screen.getByLabelText('Quiz ID').textContent).toBe('XYZ789')
    expect(hub.invoked('WatchQuiz')[0]?.args).toEqual(['XYZ789'])
  })
})

describe('Organizer live view (AC33)', () => {
  it('shows players joining live and enables Start only with at least one player', async () => {
    let started = false
    const { calls, hub } = renderApp(
      '/host/ABC234',
      {
        'POST /api/sessions/ABC234/start': () => {
          started = true
          return { status: 200, body: { state: 'Running' } }
        },
      },
      watching(snapshot()),
    )
    const user = userEvent.setup()

    const start = (await screen.findByRole('button', { name: 'Start' })) as HTMLButtonElement
    expect(start.disabled).toBe(true)
    expect(screen.getByText('0 players')).toBeTruthy()

    act(() => hub.emit('PlayerJoined', { username: 'bob', playerCount: 1 }))

    const players = within(screen.getByRole('list', { name: 'Players' })).getAllByRole('listitem')
    expect(players.map((p) => p.textContent)).toEqual(['bob'])
    expect(screen.getByText('1 player')).toBeTruthy()
    expect(start.disabled).toBe(false)

    act(() => hub.emit('PlayerJoined', { username: 'carol', playerCount: 2 }))
    act(() => hub.emit('PlayerOnlineChanged', { username: 'bob', online: false }))
    expect(within(screen.getByRole('list', { name: 'Players' })).getAllByRole('listitem').map((p) => p.textContent)).toEqual([
      'bob (offline)',
      'carol',
    ])

    await user.click(start)
    expect(started).toBe(true)
    expect(calls.filter((c) => c.path === '/api/sessions/ABC234/start')).toHaveLength(1)
  })

  it('shows the reason when Start is rejected', async () => {
    renderApp(
      '/host/ABC234',
      { 'POST /api/sessions/ABC234/start': { status: 409, body: { title: 'NoPlayers', status: 409 } } },
      watching(snapshot({ players: [{ username: 'bob', online: false }] })),
    )
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Start' }))

    expect((await screen.findByRole('alert')).textContent).toBe('At least one player must join before you start.')
  })

  it('shows the question, answers in / eligible and the full board after close', async () => {
    const hub = watching(snapshot({ players: [{ username: 'bob', online: true }] }))
    renderApp('/host/ABC234', {}, hub)
    expect(await screen.findByRole('button', { name: 'Start' })).toBeTruthy()

    act(() => hub.emit('QuestionStarted', question))
    expect(screen.getByRole('heading', { level: 3, name: 'chien' })).toBeTruthy()
    expect(screen.getByText('Question 3 of 10')).toBeTruthy()
    expect(screen.getByRole('timer').textContent).toBe('15 s')
    expect(screen.queryByRole('button', { name: 'Start' })).toBeNull()

    act(() => hub.emit('AnswersCount', { questionId: 'q1', answered: 1, eligible: 2 }))
    expect(screen.getByText('Answers in: 1 / 2')).toBeTruthy()
    act(() => hub.emit('AnswersCount', { questionId: 'q1', answered: 2, eligible: 2 }))
    expect(screen.getByText('Answers in: 2 / 2')).toBeTruthy()

    act(() => hub.emit('QuestionClosed', { questionId: 'q1', correctOptionId: 'o2', leaderboard: board(12) }))
    expect(screen.getByText('Correct answer: dog')).toBeTruthy()
    const rows = within(screen.getByRole('table', { name: 'Leaderboard' })).getAllByRole('row')
    expect(rows).toHaveLength(13) // header + all 12 players, not only a top 10
    expect(rows[12]?.textContent).toContain('p12')
  })

  it('resumes a running quiz from the snapshot', async () => {
    renderApp(
      '/host/ABC234',
      {},
      watching(
        snapshot({
          state: 'Running',
          players: [{ username: 'bob', online: true }],
          currentQuestion: question,
          answersCount: { answered: 0, eligible: 1 },
          leaderboard: board(1),
        }),
      ),
    )

    expect(await screen.findByRole('heading', { level: 3, name: 'chien' })).toBeTruthy()
    expect(screen.getByText('Answers in: 0 / 1')).toBeTruthy()
  })

  it('shows the podium and the full standings at finish', async () => {
    const hub = watching(snapshot({ state: 'Running', players: [{ username: 'bob', online: true }] }))
    renderApp('/host/ABC234', {}, hub)
    expect(await screen.findByText('1 player')).toBeTruthy()

    act(() => hub.emit('QuizFinished', { leaderboard: board(12) }))

    expect(screen.getByRole('heading', { level: 3, name: 'Quiz finished' })).toBeTruthy()
    const podium = within(screen.getByRole('list', { name: 'Podium' })).getAllByRole('listitem')
    expect(podium.map((p) => p.textContent)).toEqual(['1st p1 2000', '2nd p2 1900', '3rd p3 1800'])
    const rows = within(screen.getByRole('table', { name: 'Leaderboard' })).getAllByRole('row')
    expect(rows).toHaveLength(13)
    expect(rows[12]?.textContent).toContain('offline')
  })

  it('shows the reason when watching is rejected', async () => {
    const hub = new FakeQuizHub()
    hub.watchReply = () => Promise.reject(hubError('NotOrganizer'))
    renderApp('/host/ABC234', {}, hub)

    expect((await screen.findByRole('alert')).textContent).toBe('Only the organizer can host this quiz.')
  })

  it('shows "Connection lost" and reconnects with Rejoin', async () => {
    const hub = watching(snapshot())
    renderApp('/host/ABC234', {}, hub)
    const user = userEvent.setup()
    expect(await screen.findByRole('button', { name: 'Start' })).toBeTruthy()

    act(() => hub.drop())
    expect(screen.getByText('Connection lost')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Rejoin' }))

    expect(await screen.findByRole('button', { name: 'Start' })).toBeTruthy()
    expect(hub.invoked('WatchQuiz')).toHaveLength(2)
  })
})
