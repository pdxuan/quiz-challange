import { useCallback, useEffect, useReducer, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { api, ApiError } from '../../api/client'
import type { BoardEntry, PlayerView, WatchSnapshot } from '../../realtime/contracts'
import { errorMessage, hubErrorCode, useHubFactory, type QuizHubClient } from '../../realtime/hub'
import { Countdown, timed, type TimedQuestion } from '../play/Countdown'
import styles from './Host.module.css'

interface HostState {
  status: 'connecting' | 'watching' | 'rejected'
  lost: boolean
  error: string | null
  state: WatchSnapshot['state']
  players: PlayerView[]
  question: TimedQuestion | null
  answers: { answered: number; eligible: number } | null
  correctOptionId: string | null
  leaderboard: BoardEntry[]
}

type Action =
  | { type: 'connecting' }
  | { type: 'snapshot'; snapshot: WatchSnapshot }
  | { type: 'rejected'; message: string }
  | { type: 'lost' }
  | { type: 'playerJoined'; username: string }
  | { type: 'online'; username: string; online: boolean }
  | { type: 'questionStarted'; question: TimedQuestion }
  | { type: 'answers'; questionId: string; answered: number; eligible: number }
  | { type: 'closed'; questionId: string; correctOptionId: string; leaderboard: BoardEntry[] }
  | { type: 'finished'; leaderboard: BoardEntry[] }

const initial: HostState = {
  status: 'connecting',
  lost: false,
  error: null,
  state: 'Lobby',
  players: [],
  question: null,
  answers: null,
  correctOptionId: null,
  leaderboard: [],
}

function reduce(s: HostState, action: Action): HostState {
  switch (action.type) {
    case 'connecting':
      return { ...s, lost: false, error: null }
    case 'snapshot': {
      const snap = action.snapshot
      return {
        ...s,
        status: 'watching',
        state: snap.state,
        players: snap.players,
        question: snap.currentQuestion ? timed(snap.currentQuestion) : null,
        answers: snap.answersCount,
        correctOptionId: null,
        leaderboard: snap.leaderboard,
      }
    }
    case 'rejected':
      return { ...s, status: 'rejected', error: action.message }
    case 'lost':
      return { ...s, lost: true }
    case 'playerJoined':
      return s.players.some((p) => p.username === action.username)
        ? s
        : { ...s, players: [...s.players, { username: action.username, online: true }] }
    case 'online':
      return { ...s, players: s.players.map((p) => (p.username === action.username ? { ...p, online: action.online } : p)) }
    case 'questionStarted':
      return { ...s, state: 'Running', question: action.question, answers: null, correctOptionId: null }
    case 'answers':
      return s.question?.questionId === action.questionId ? { ...s, answers: { answered: action.answered, eligible: action.eligible } } : s
    case 'closed':
      return { ...s, correctOptionId: action.correctOptionId, leaderboard: action.leaderboard }
    case 'finished':
      return { ...s, state: 'Finished', question: null, answers: null, correctOptionId: null, leaderboard: action.leaderboard }
  }
}

export function HostLive() {
  const { quizId = '' } = useParams()
  const createHub = useHubFactory()
  const [s, dispatch] = useReducer(reduce, initial)
  const [startError, setStartError] = useState<string | null>(null)
  const [starting, setStarting] = useState(false)
  const hubRef = useRef<QuizHubClient | null>(null)
  const live = useRef(false)

  const watch = useCallback(
    async (hub: QuizHubClient, isLive: () => boolean) => {
      dispatch({ type: 'connecting' })
      try {
        await hub.connect()
      } catch {
        if (isLive()) dispatch({ type: 'lost' })
        return
      }
      try {
        const snapshot = await hub.watchQuiz(quizId)
        if (isLive()) dispatch({ type: 'snapshot', snapshot })
      } catch (error) {
        if (isLive()) dispatch({ type: 'rejected', message: errorMessage(hubErrorCode(error)) })
      }
    },
    [quizId],
  )

  useEffect(() => {
    const hub = createHub()
    hubRef.current = hub
    let disposed = false
    live.current = true
    const isLive = () => !disposed

    hub.on('PlayerJoined', (e) => isLive() && dispatch({ type: 'playerJoined', username: e.username }))
    hub.on('PlayerOnlineChanged', (e) => isLive() && dispatch({ type: 'online', username: e.username, online: e.online }))
    hub.on('QuestionStarted', (q) => isLive() && dispatch({ type: 'questionStarted', question: timed(q) }))
    hub.on('AnswersCount', (e) => isLive() && dispatch({ type: 'answers', ...e }))
    hub.on('QuestionClosed', (e) => isLive() && 'leaderboard' in e && dispatch({ type: 'closed', ...e }))
    hub.on('QuizFinished', (e) => isLive() && 'leaderboard' in e && dispatch({ type: 'finished', leaderboard: e.leaderboard }))
    hub.onClose(() => isLive() && dispatch({ type: 'lost' }))
    void watch(hub, isLive)

    return () => {
      disposed = true
      live.current = false
      void hub.disconnect()
    }
  }, [createHub, watch])

  async function start() {
    setStarting(true)
    setStartError(null)
    try {
      await api.startSession(quizId)
    } catch (e) {
      setStartError(e instanceof ApiError ? errorMessage(e.code) : 'Could not start the quiz.')
    } finally {
      setStarting(false)
    }
  }

  function rejoin() {
    const hub = hubRef.current
    if (hub) void watch(hub, () => live.current)
  }

  const count = s.players.length
  return (
    <section className={styles.panel}>
      <h2>Hosting {quizId}</h2>
      {s.lost && (
        <div className={styles.lost}>
          <p>Connection lost</p>
          <button type="button" onClick={rejoin}>
            Rejoin
          </button>
        </div>
      )}
      {s.status === 'connecting' && <p>Connecting...</p>}
      {s.status === 'rejected' && (
        <p role="alert" className={styles.error}>
          {s.error}
        </p>
      )}
      {s.status === 'watching' && (
        <>
          {s.state === 'Lobby' && (
            <>
              <p>Players join with this quiz ID:</p>
              <p aria-label="Quiz ID" className={styles.quizId}>
                {quizId}
              </p>
              <button type="button" onClick={start} disabled={count === 0 || starting}>
                Start
              </button>
              {startError && (
                <p role="alert" className={styles.error}>
                  {startError}
                </p>
              )}
            </>
          )}
          {s.state === 'Running' && s.question && <Question host={s} question={s.question} />}
          {s.state === 'Finished' && <Podium leaderboard={s.leaderboard} />}
          <p>
            {count} {count === 1 ? 'player' : 'players'}
          </p>
          <ul aria-label="Players" className={styles.players}>
            {s.players.map((p) => (
              <li key={p.username} className={p.online ? undefined : styles.offline}>
                {p.online ? p.username : `${p.username} (offline)`}
              </li>
            ))}
          </ul>
          {s.state !== 'Lobby' && s.leaderboard.length > 0 && <Board entries={s.leaderboard} />}
        </>
      )}
    </section>
  )
}

function Question({ host, question }: { host: HostState; question: TimedQuestion }) {
  const correct = question.options.find((o) => o.optionId === host.correctOptionId)
  return (
    <>
      <p>
        Question {question.index + 1} of {question.total}
      </p>
      <h3 className={styles.prompt}>{question.prompt}</h3>
      {correct ? <p className={styles.correct}>Correct answer: {correct.text}</p> : <Countdown deadline={question.deadline} />}
      <ul className={styles.options}>
        {question.options.map((o) => (
          <li key={o.optionId} className={o.optionId === host.correctOptionId ? styles.correct : undefined}>
            {o.text}
          </li>
        ))}
      </ul>
      {host.answers && (
        <p className={styles.big}>
          Answers in: {host.answers.answered} / {host.answers.eligible}
        </p>
      )}
    </>
  )
}

/** Podium places follow the shared ranks, so a tie shows "1st, 1st, 3rd". */
function ordinal(rank: number) {
  return rank === 1 ? '1st' : rank === 2 ? '2nd' : rank === 3 ? '3rd' : `${rank}th`
}

function Podium({ leaderboard }: { leaderboard: BoardEntry[] }) {
  return (
    <>
      <h3>Quiz finished</h3>
      <ol aria-label="Podium" className={styles.podium}>
        {leaderboard.slice(0, 3).map((e) => (
          <li key={e.username}>{`${ordinal(e.rank)} ${e.username} ${e.totalPoints}`}</li>
        ))}
      </ol>
    </>
  )
}

function Board({ entries }: { entries: BoardEntry[] }) {
  return (
    <table aria-label="Leaderboard" className={styles.board}>
      <thead>
        <tr>
          <th>Rank</th>
          <th>Player</th>
          <th>Points</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((e) => (
          <tr key={e.username}>
            <td>{e.rank}</td>
            <td>{e.username}</td>
            <td>{e.totalPoints}</td>
            <td>{e.online ? 'online' : 'offline'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
