// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by the view tests (error codes) and a manual run (docs/ai-collab/s5.md).
import { createContext, useContext } from 'react'
import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr'
import type { AnswerAccepted, HubEventName, HubEvents, JoinResult, WatchSnapshot } from './contracts'

/** The views talk to the hub only through this interface; tests use FakeQuizHub. */
export interface QuizHubClient {
  connect(): Promise<void>
  disconnect(): Promise<void>
  joinQuiz(quizId: string): Promise<JoinResult>
  watchQuiz(quizId: string): Promise<WatchSnapshot>
  submitAnswer(quizId: string, questionId: string, optionId: string, submissionId: string): Promise<AnswerAccepted>
  on<E extends HubEventName>(event: E, handler: (payload: HubEvents[E]) => void): void
  /** Called when the connection drops (not after `disconnect`). No auto-reconnect: the view offers Rejoin. */
  onClose(handler: () => void): void
}

/** SignalR implementation over `/hubs/quiz` (same origin, auth cookie). */
export class SignalRQuizHub implements QuizHubClient {
  private readonly connection: HubConnection
  private stopping = false

  constructor(url = '/hubs/quiz') {
    this.connection = new HubConnectionBuilder().withUrl(url).build()
  }

  connect() {
    this.stopping = false
    return this.connection.start()
  }

  disconnect() {
    this.stopping = true
    return this.connection.stop()
  }

  joinQuiz(quizId: string) {
    return this.connection.invoke<JoinResult>('JoinQuiz', quizId)
  }

  watchQuiz(quizId: string) {
    return this.connection.invoke<WatchSnapshot>('WatchQuiz', quizId)
  }

  submitAnswer(quizId: string, questionId: string, optionId: string, submissionId: string) {
    return this.connection.invoke<AnswerAccepted>('SubmitAnswer', quizId, questionId, optionId, submissionId)
  }

  on<E extends HubEventName>(event: E, handler: (payload: HubEvents[E]) => void) {
    this.connection.on(event, handler)
  }

  onClose(handler: () => void) {
    this.connection.onclose(() => {
      if (!this.stopping) {
        handler()
      }
    })
  }
}

/** Creates one hub client per view; tests provide a factory that returns a fake. */
export const HubFactoryContext = createContext<() => QuizHubClient>(() => new SignalRQuizHub())

export function useHubFactory() {
  return useContext(HubFactoryContext)
}

const HubErrorMarker = 'HubException: '

/** The server's error code from a failed hub call ("...HubException: QuizNotFound" -> "QuizNotFound"). */
export function hubErrorCode(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error)
  const at = message.lastIndexOf(HubErrorMarker)
  return at < 0 ? 'ServerError' : message.slice(at + HubErrorMarker.length).trim()
}

const messages: Record<string, string> = {
  QuizNotFound: 'Quiz not found. Check the quiz ID.',
  QuizFinished: 'This quiz has already finished.',
  SessionFull: 'This quiz is full.',
  IsOrganizer: 'You are the organizer of this quiz. Open it from Host a quiz.',
  NotOrganizer: 'Only the organizer can host this quiz.',
  NotInLobby: 'The quiz has already started.',
  NoPlayers: 'At least one player must join before you start.',
  NotJoined: 'You have not joined this quiz.',
  NotStarted: 'The quiz has not started yet.',
  NotCurrentQuestion: 'This question is no longer open.',
  NotEligible: 'You joined during this question. You can answer from the next one.',
  QuestionClosed: "Time's up for this question.",
  AlreadyAnswered: 'You already answered this question.',
  Invalid: 'That request was not valid.',
  ServerError: 'Something went wrong on the server. Please try again.',
}

/** A readable message for a server error code (hub or REST). */
export function errorMessage(code: string): string {
  return messages[code] ?? `Something went wrong (${code}).`
}
