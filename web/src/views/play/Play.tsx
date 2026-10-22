// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by views/play/Play.test.tsx, mutations (docs/ai-collab/s5.md).
import { useCallback, useEffect, useReducer, useRef } from 'react'
import { Link, useParams } from 'react-router'
import type { BoardEntry, JoinResult, PlayerQuestionClosed, PlayerQuizFinished } from '../../realtime/contracts'
import { errorMessage, hubErrorCode, useHubFactory, type QuizHubClient } from '../../realtime/hub'
import { Countdown, timed, type TimedQuestion } from '../../realtime/Countdown'
import styles from './Play.module.css'

interface PlayState {
  phase: 'connecting' | 'lobby' | 'question' | 'waiting' | 'reveal' | 'final' | 'rejected'
  lost: boolean
  error: string | null
  notice: string | null
  playerCount: number
  question: TimedQuestion | null
  canAnswer: boolean
  chosen: string | null
  locked: boolean
  you: { totalPoints: number; rank: number }
  top10: BoardEntry[]
  reveal: PlayerQuestionClosed | null
  final: PlayerQuizFinished | null
}

type Action =
  | { type: 'connecting' }
  | { type: 'joined'; result: JoinResult }
  | { type: 'rejected'; message: string }
  | { type: 'lost' }
  | { type: 'playerJoined'; playerCount: number }
  | { type: 'questionStarted'; question: TimedQuestion }
  | { type: 'chosen'; optionId: string }
  | { type: 'accepted' }
  | { type: 'answerRejected'; message: string; retry: boolean }
  | { type: 'closed'; payload: PlayerQuestionClosed }
  | { type: 'finished'; payload: PlayerQuizFinished }

/** Rejections after which the player may pick again; every other code is final for the question. */
const RETRYABLE = new Set(['Invalid', 'ServerError'])

const initial: PlayState = {
  phase: 'connecting',
  lost: false,
  error: null,
  notice: null,
  playerCount: 0,
  question: null,
  canAnswer: false,
  chosen: null,
  locked: false,
  you: { totalPoints: 0, rank: 0 },
  top10: [],
  reveal: null,
  final: null,
}

function reduce(state: PlayState, action: Action): PlayState {
  switch (action.type) {
    case 'connecting':
      return { ...state, lost: false, error: null }
    case 'joined': {
      const r = action.result
      const question = r.currentQuestion ? timed(r.currentQuestion) : null
      const sameQuestion = question !== null && state.question?.questionId === question.questionId
      return {
        ...state,
        phase: r.state === 'Lobby' ? 'lobby' : question ? 'question' : 'waiting',
        playerCount: r.playerCount,
        question,
        canAnswer: r.canAnswerCurrent,
        chosen: sameQuestion ? state.chosen : null,
        locked: sameQuestion ? state.locked : false,
        you: r.you,
        top10: r.top10,
        reveal: null,
      }
    }
    case 'rejected':
      return { ...state, phase: 'rejected', error: action.message }
    case 'lost':
      return { ...state, lost: true }
    case 'playerJoined':
      return { ...state, playerCount: action.playerCount }
    case 'questionStarted':
      return { ...state, phase: 'question', question: action.question, canAnswer: true, chosen: null, locked: false, notice: null, reveal: null }
    case 'chosen':
      return { ...state, chosen: action.optionId, notice: null }
    case 'accepted':
      return { ...state, locked: true }
    case 'answerRejected':
      // A final rejection (closed, already answered, ...) keeps the options locked; only a retryable one frees them.
      return { ...state, chosen: action.retry ? null : state.chosen, notice: action.message }
    case 'closed':
      return { ...state, phase: 'reveal', reveal: action.payload, notice: null, you: { totalPoints: action.payload.totalPoints, rank: action.payload.rank }, top10: action.payload.top10 }
    case 'finished':
      return { ...state, phase: 'final', final: action.payload, notice: null, top10: action.payload.top10 }
  }
}

export function Play() {
  const { quizId = '' } = useParams()
  const createHub = useHubFactory()
  const [state, dispatch] = useReducer(reduce, initial)
  const hubRef = useRef<QuizHubClient | null>(null)

  const join = useCallback(
    async (hub: QuizHubClient, isLive: () => boolean) => {
      dispatch({ type: 'connecting' })
      try {
        await hub.connect()
      } catch {
        if (isLive()) dispatch({ type: 'lost' })
        return
      }
      try {
        const result = await hub.joinQuiz(quizId)
        if (isLive()) dispatch({ type: 'joined', result })
      } catch (error) {
        if (isLive()) dispatch({ type: 'rejected', message: errorMessage(hubErrorCode(error)) })
      }
    },
    [quizId],
  )

  const live = useRef(false)

  useEffect(() => {
    const hub = createHub()
    hubRef.current = hub
    let disposed = false
    live.current = true
    const isLive = () => !disposed

    hub.on('PlayerJoined', (e) => isLive() && dispatch({ type: 'playerJoined', playerCount: e.playerCount }))
    hub.on('QuestionStarted', (q) => isLive() && dispatch({ type: 'questionStarted', question: timed(q) }))
    hub.on('QuestionClosed', (e) => isLive() && 'correct' in e && dispatch({ type: 'closed', payload: e }))
    hub.on('QuizFinished', (e) => isLive() && 'rank' in e && dispatch({ type: 'finished', payload: e }))
    hub.onClose(() => isLive() && dispatch({ type: 'lost' }))
    void join(hub, isLive)

    return () => {
      disposed = true
      live.current = false
      void hub.disconnect()
    }
  }, [createHub, join])

  async function answer(optionId: string) {
    const hub = hubRef.current
    const question = state.question
    if (!hub || !question) return
    dispatch({ type: 'chosen', optionId })
    try {
      await hub.submitAnswer(quizId, question.questionId, optionId, crypto.randomUUID())
      if (live.current) dispatch({ type: 'accepted' })
    } catch (error) {
      const code = hubErrorCode(error)
      if (live.current) dispatch({ type: 'answerRejected', message: errorMessage(code), retry: RETRYABLE.has(code) })
    }
  }

  function rejoin() {
    const hub = hubRef.current
    if (hub) void join(hub, () => live.current)
  }

  return (
    <section className={`card ${styles.panel}`}>
      <h2 className={styles.title}>Playing {quizId}</h2>
      {state.lost && (
        <div className={styles.lost}>
          <p>Connection lost</p>
          <button type="button" onClick={rejoin}>
            Rejoin
          </button>
        </div>
      )}
      <PhaseView state={state} onAnswer={answer} />
    </section>
  )
}

function PhaseView({ state, onAnswer }: { state: PlayState; onAnswer: (optionId: string) => void }) {
  switch (state.phase) {
    case 'connecting':
      return <p className="muted">Joining...</p>
    case 'rejected':
      return (
        <>
          <p role="alert" className={styles.error}>
            {state.error}
          </p>
          <Link to="/">Back to home</Link>
        </>
      )
    case 'lobby':
      return (
        <div className={styles.waiting}>
          <p className={styles.waitingTitle}>Waiting for the host to start...</p>
          <p className="muted">
            {state.playerCount} {state.playerCount === 1 ? 'player' : 'players'} in the lobby
          </p>
        </div>
      )
    case 'waiting':
      return (
        <>
          <p className={styles.waitingTitle}>Waiting for the next question...</p>
          <Standing total={state.you.totalPoints} rank={state.you.rank} />
          <Top10 entries={state.top10} />
        </>
      )
    case 'question':
      return state.question && <QuestionPanel state={state} question={state.question} onAnswer={onAnswer} />
    case 'reveal':
      return state.reveal && <Reveal reveal={state.reveal} question={state.question} />
    case 'final':
      return (
        state.final && (
          <>
            <h3 className={styles.finished}>Quiz finished</h3>
            <Standing total={state.final.totalPoints} rank={state.final.rank} big />
            <Top10 entries={state.final.top10} />
            <Link to="/">Back to home</Link>
          </>
        )
      )
  }
}

const LETTERS = ['A', 'B', 'C', 'D']

function QuestionPanel({ state, question, onAnswer }: { state: PlayState; question: TimedQuestion; onAnswer: (optionId: string) => void }) {
  const disabled = !state.canAnswer || state.chosen !== null
  return (
    <>
      <div className={styles.meta}>
        <p>
          Question {question.index + 1} of {question.total}
        </p>
        <Countdown deadline={question.deadline} />
      </div>
      <h3 className={styles.headword}>{question.prompt}</h3>
      <div role="group" aria-label="Options" className={styles.options}>
        {question.options.map((o, i) => (
          <button
            key={o.optionId}
            type="button"
            className={`${styles.option} ${o.optionId === state.chosen ? styles.chosen : ''}`}
            data-letter={LETTERS[i]}
            disabled={disabled}
            onClick={() => onAnswer(o.optionId)}
          >
            {o.text}
          </button>
        ))}
      </div>
      {!state.canAnswer && <p className="muted">You joined during this question. You can answer from the next one.</p>}
      {state.locked && <p className={styles.locked}>Answer locked in</p>}
      {state.notice && (
        <p role="alert" className={styles.error}>
          {state.notice}
        </p>
      )}
    </>
  )
}

function Reveal({ reveal, question }: { reveal: PlayerQuestionClosed; question: TimedQuestion | null }) {
  const text = (optionId: string) => question?.options.find((o) => o.optionId === optionId)?.text ?? optionId
  const verdict = reveal.yourOptionId === null ? 'No answer' : reveal.correct ? 'Correct!' : 'Wrong'
  return (
    <>
      {question && <h3 className={styles.headword}>{question.prompt}</h3>}
      <p className={styles.answer}>Correct answer: {text(reveal.correctOptionId)}</p>
      <div className={styles.verdictRow}>
        <p className={reveal.correct ? styles.right : styles.wrong}>{verdict}</p>
        <p className={styles.points}>+{reveal.points} points</p>
      </div>
      <Standing total={reveal.totalPoints} rank={reveal.rank} />
      <Top10 entries={reveal.top10} />
    </>
  )
}

function Standing({ total, rank, big = false }: { total: number; rank: number; big?: boolean }) {
  return (
    <div className={`${styles.standing} ${big ? styles.standingBig : ''}`}>
      <p>Total: {total}</p>
      <p>Rank: {rank}</p>
    </div>
  )
}

function Top10({ entries }: { entries: BoardEntry[] }) {
  return (
    <ol aria-label="Top 10" className={styles.board}>
      {entries.map((e) => (
        <li key={e.username}>
          <span className={styles.rank}>{e.rank}</span>{' '}
          <span className={styles.name}>{e.username}</span>{' '}
          <span className={styles.score}>{e.totalPoints}</span>
        </li>
      ))}
    </ol>
  )
}
