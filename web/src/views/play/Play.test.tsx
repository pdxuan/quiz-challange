import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import App from '../../App'
import { fakeFetch, type FakeRoutes } from '../../api/fakeFetch'
import { FakeQuizHub, hubError } from '../../realtime/fakeHub'
import { HubFactoryContext } from '../../realtime/hub'
import type { BoardEntry, JoinResult, QuestionView } from '../../realtime/contracts'

// The server clock is far from the client clock on purpose: the countdown must use only endsAt - serverNow.
const serverNow = '2001-01-01T10:00:00.000Z'

function question(overrides: Partial<QuestionView> = {}): QuestionView {
  return {
    questionId: 'q1',
    index: 0,
    total: 10,
    prompt: 'chien',
    options: [
      { optionId: 'o1', text: 'cat' },
      { optionId: 'o2', text: 'dog' },
      { optionId: 'o3', text: 'bird' },
      { optionId: 'o4', text: 'fish' },
    ],
    endsAt: '2001-01-01T10:00:15.000Z',
    serverNow,
    ...overrides,
  }
}

function lobby(overrides: Partial<JoinResult> = {}): JoinResult {
  return {
    username: 'alice',
    isOrganizer: false,
    state: 'Lobby',
    playerCount: 1,
    players: [{ username: 'alice', online: true }],
    currentQuestion: null,
    canAnswerCurrent: false,
    you: { totalPoints: 0, rank: 1 },
    top10: [{ rank: 1, username: 'alice', totalPoints: 0, online: true }],
    ...overrides,
  }
}

function board(count: number): BoardEntry[] {
  return Array.from({ length: count }, (_, i) => ({ rank: i + 1, username: `p${i + 1}`, totalPoints: 1000 - i * 10, online: true }))
}

function renderPlay(hub: FakeQuizHub, routes: FakeRoutes = {}) {
  const fake = fakeFetch({ 'GET /api/auth/me': { status: 200, body: { userId: 'u1', username: 'alice' } }, ...routes })
  vi.stubGlobal('fetch', fake.fetch)
  render(
    <HubFactoryContext value={() => hub}>
      <MemoryRouter initialEntries={['/play/ABC234']}>
        <App />
      </MemoryRouter>
    </HubFactoryContext>,
  )
  return fake.calls
}

function optionButtons() {
  return within(screen.getByRole('group', { name: 'Options' })).getAllByRole('button') as HTMLButtonElement[]
}

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('Player view (AC32)', () => {
  it('goes lobby -> question -> locked in -> reveal -> final', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby())
    renderPlay(hub)
    const user = userEvent.setup()

    expect(await screen.findByText('Waiting for the host to start...')).toBeTruthy()
    expect(hub.invoked('JoinQuiz')[0]?.args).toEqual(['ABC234'])

    act(() => hub.emit('QuestionStarted', question()))
    expect(screen.getByRole('heading', { level: 3, name: 'chien' })).toBeTruthy()
    expect(screen.getByText('Question 1 of 10')).toBeTruthy()
    expect(optionButtons().map((b) => b.textContent)).toEqual(['cat', 'dog', 'bird', 'fish'])
    expect(screen.getByRole('timer').textContent).toBe('15 s')

    await user.click(screen.getByRole('button', { name: 'dog' }))

    expect(await screen.findByText('Answer locked in')).toBeTruthy()
    const [call] = hub.invoked('SubmitAnswer')
    expect(call?.args.slice(0, 3)).toEqual(['ABC234', 'q1', 'o2'])
    expect(call?.args[3]).toMatch(/^[0-9a-f-]{36}$/)
    expect(optionButtons().every((b) => b.disabled)).toBe(true)

    act(() =>
      hub.emit('QuestionClosed', {
        questionId: 'q1',
        correctOptionId: 'o2',
        yourOptionId: 'o2',
        correct: true,
        points: 812,
        totalPoints: 812,
        rank: 2,
        top10: board(10),
      }),
    )
    expect(screen.getByText('Correct!')).toBeTruthy()
    expect(screen.getByText('Correct answer: dog')).toBeTruthy()
    expect(screen.getByText('+812 points')).toBeTruthy()
    expect(screen.getByText('Total: 812')).toBeTruthy()
    expect(screen.getByText('Rank: 2')).toBeTruthy()
    const top10 = within(screen.getByRole('list', { name: 'Top 10' })).getAllByRole('listitem')
    expect(top10).toHaveLength(10)
    expect(top10[0]?.textContent).toBe('1 p1 1000') // rank, name and points read as separate words
    expect(top10[9]?.textContent).toBe('10 p10 910')

    act(() => hub.emit('QuizFinished', { totalPoints: 1600, rank: 3, top10: board(10) }))
    expect(screen.getByRole('heading', { level: 3, name: 'Quiz finished' })).toBeTruthy()
    expect(screen.getByText('Total: 1600')).toBeTruthy()
    expect(screen.getByText('Rank: 3')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Top 10' })).getAllByRole('listitem')).toHaveLength(10)
  })

  it('counts down from endsAt - serverNow, not from the client clock', async () => {
    vi.useFakeTimers({ now: new Date('2030-06-01T00:00:00Z') })
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby())
    renderPlay(hub)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })

    act(() => hub.emit('QuestionStarted', question({ endsAt: '2001-01-01T10:00:10.000Z' })))
    expect(screen.getByRole('timer').textContent).toBe('10 s')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000)
    })
    expect(screen.getByRole('timer').textContent).toBe('7 s')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20000)
    })
    expect(screen.getByRole('timer').textContent).toBe('0 s')
  })

  it('shows a reveal with the wrong pick and 0 points', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby({ state: 'Running', currentQuestion: question(), canAnswerCurrent: true }))
    renderPlay(hub)

    expect(await screen.findByRole('heading', { level: 3, name: 'chien' })).toBeTruthy()
    act(() =>
      hub.emit('QuestionClosed', {
        questionId: 'q1',
        correctOptionId: 'o2',
        yourOptionId: null,
        correct: false,
        points: 0,
        totalPoints: 0,
        rank: 1,
        top10: board(1),
      }),
    )

    expect(screen.getByText('No answer')).toBeTruthy()
    expect(screen.getByText('Correct answer: dog')).toBeTruthy()
    expect(screen.getByText('+0 points')).toBeTruthy()
  })

  it('a late joiner sees the current question with options disabled', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby({ state: 'Running', currentQuestion: question(), canAnswerCurrent: false }))
    renderPlay(hub)

    expect(await screen.findByText('You joined during this question. You can answer from the next one.')).toBeTruthy()
    expect(optionButtons().every((b) => b.disabled)).toBe(true)

    act(() => hub.emit('QuestionStarted', question({ questionId: 'q2', index: 1, prompt: 'chat' })))
    expect(optionButtons().every((b) => !b.disabled)).toBe(true)
  })

  it('shows the reason when an answer is rejected', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby({ state: 'Running', currentQuestion: question(), canAnswerCurrent: true }))
    hub.submitReply = () => Promise.reject(hubError('QuestionClosed'))
    renderPlay(hub)
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'cat' }))

    expect((await screen.findByRole('alert')).textContent).toBe("Time's up for this question.")
  })

  it.each(['QuestionClosed', 'AlreadyAnswered', 'NotEligible', 'NotCurrentQuestion'])(
    'keeps the options disabled after a final rejection (%s)',
    async (code) => {
      const hub = new FakeQuizHub()
      hub.joinReply = () => Promise.resolve(lobby({ state: 'Running', currentQuestion: question(), canAnswerCurrent: true }))
      hub.submitReply = () => Promise.reject(hubError(code))
      renderPlay(hub)
      const user = userEvent.setup()

      await user.click(await screen.findByRole('button', { name: 'cat' }))
      await screen.findByRole('alert')

      for (const name of ['cat', 'dog', 'bird', 'fish']) {
        expect((screen.getByRole('button', { name }) as HTMLButtonElement).disabled).toBe(true)
      }
    },
  )

  it('lets the player try again after a retryable rejection (ServerError)', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby({ state: 'Running', currentQuestion: question(), canAnswerCurrent: true }))
    hub.submitReply = () => Promise.reject(hubError('ServerError'))
    renderPlay(hub)
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'cat' }))
    await screen.findByRole('alert')

    expect((screen.getByRole('button', { name: 'dog' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it.each([
    ['QuizNotFound', 'Quiz not found. Check the quiz ID.'],
    ['QuizFinished', 'This quiz has already finished.'],
    ['SessionFull', 'This quiz is full.'],
  ])('shows the reason when the join fails with %s', async (code, message) => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.reject(hubError(code))
    renderPlay(hub)

    expect((await screen.findByRole('alert')).textContent).toBe(message)
  })

  it('shows "Connection lost" with Rejoin, and Rejoin reconnects and joins again', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby())
    renderPlay(hub)
    const user = userEvent.setup()
    expect(await screen.findByText('Waiting for the host to start...')).toBeTruthy()

    act(() => hub.drop())

    expect(screen.getByText('Connection lost')).toBeTruthy()
    hub.joinReply = () => Promise.resolve(lobby({ state: 'Running', currentQuestion: question(), canAnswerCurrent: true, you: { totalPoints: 500, rank: 1 } }))
    await user.click(screen.getByRole('button', { name: 'Rejoin' }))

    expect(await screen.findByRole('heading', { level: 3, name: 'chien' })).toBeTruthy()
    expect(screen.queryByText('Connection lost')).toBeNull()
    expect(hub.invoked('connect')).toHaveLength(2)
    expect(hub.invoked('JoinQuiz')).toHaveLength(2)
  })
})

describe('Lobby with the organizer playing too', () => {
  it('shows the organizer the quiz ID, every player by name, Start and the organizer board', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () => Promise.resolve(lobby({ isOrganizer: true, players: [{ username: 'alice', online: true }] }))
    const calls = renderPlay(hub, { 'POST /api/sessions/ABC234/start': { status: 200, body: { state: 'Running' } } })
    const user = userEvent.setup()

    expect((await screen.findByLabelText('Quiz ID')).textContent).toBe('ABC234')
    expect((screen.getByRole('button', { name: 'Start' }) as HTMLButtonElement).disabled).toBe(true) // alone
    expect(screen.getByText('Start opens once two players are online.')).toBeTruthy()
    act(() => hub.emit('PlayerJoined', { username: 'bob', playerCount: 2 }))
    const names = within(screen.getByRole('list', { name: 'Players' })).getAllByRole('listitem')
    expect(names.map((n) => n.textContent)).toEqual(['alice', 'bob'])
    const boardLink = screen.getByRole('link', { name: 'Organizer board' })
    expect(boardLink.getAttribute('href')).toBe('/host/ABC234')
    expect(boardLink.getAttribute('target')).toBe('_blank') // leaving this tab would take the organizer offline as a player

    const start = screen.getByRole('button', { name: 'Start' }) as HTMLButtonElement
    expect(start.disabled).toBe(false)
    act(() => hub.emit('PlayerOnlineChanged', { username: 'bob', online: false }))
    expect(start.disabled).toBe(true) // joined is not enough: the server counts online players
    expect(within(screen.getByRole('list', { name: 'Players' })).getAllByRole('listitem').map((n) => n.textContent)).toEqual([
      'alice',
      'bob (offline)',
    ])
    act(() => hub.emit('PlayerOnlineChanged', { username: 'bob', online: true }))

    await user.click(start)

    expect(calls.filter((c) => c.method === 'POST' && c.path === '/api/sessions/ABC234/start')).toHaveLength(1)
  })

  it('shows why Start failed', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () =>
      Promise.resolve(lobby({ isOrganizer: true, playerCount: 2, players: [{ username: 'alice', online: true }, { username: 'bob', online: true }] }))
    renderPlay(hub, { 'POST /api/sessions/ABC234/start': { status: 409, body: { title: 'NoPlayers' } } })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Start' }))

    expect((await screen.findByRole('alert')).textContent).toBe('At least two players must be online before you start.') // e.g. bob just left
  })

  it('shows a player the names but no Start and no organizer board', async () => {
    const hub = new FakeQuizHub()
    hub.joinReply = () =>
      Promise.resolve(lobby({ players: [{ username: 'olivia', online: true }, { username: 'alice', online: true }], playerCount: 2 }))
    renderPlay(hub)

    expect(await screen.findByText('Waiting for the host to start...')).toBeTruthy()
    const names = within(screen.getByRole('list', { name: 'Players' })).getAllByRole('listitem')
    expect(names.map((n) => n.textContent)).toEqual(['olivia', 'alice'])
    expect(screen.queryByRole('button', { name: 'Start' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Organizer board' })).toBeNull()
  })
})
