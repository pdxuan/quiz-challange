import type { AnswerAccepted, HubEventName, HubEvents, JoinResult, WatchSnapshot } from './contracts'
import type { QuizHubClient } from './hub'

/** A hub error as the SignalR JS client reports it: the code follows "HubException: ". */
export function hubError(code: string): Error {
  return new Error(`An unexpected error occurred invoking 'X' on the server. HubException: ${code}`)
}

/**
 * Test double for the hub: replies come from the functions a test sets, `emit` pushes a server
 * event, `drop` simulates a lost connection. Every call is recorded.
 */
export class FakeQuizHub implements QuizHubClient {
  readonly calls: { method: string; args: unknown[] }[] = []
  connected = false

  joinReply: (quizId: string) => Promise<JoinResult> = () => Promise.reject(hubError('QuizNotFound'))
  watchReply: (quizId: string) => Promise<WatchSnapshot> = () => Promise.reject(hubError('QuizNotFound'))
  submitReply: (questionId: string, optionId: string, submissionId: string) => Promise<AnswerAccepted> = (questionId, _optionId, submissionId) =>
    Promise.resolve({ questionId, submissionId })

  private readonly handlers = new Map<string, ((payload: never) => void)[]>()
  private readonly closeHandlers: (() => void)[] = []

  async connect() {
    this.calls.push({ method: 'connect', args: [] })
    this.connected = true
  }

  async disconnect() {
    this.calls.push({ method: 'disconnect', args: [] })
    this.connected = false
  }

  joinQuiz(quizId: string) {
    this.calls.push({ method: 'JoinQuiz', args: [quizId] })
    return this.joinReply(quizId)
  }

  watchQuiz(quizId: string) {
    this.calls.push({ method: 'WatchQuiz', args: [quizId] })
    return this.watchReply(quizId)
  }

  submitAnswer(quizId: string, questionId: string, optionId: string, submissionId: string) {
    this.calls.push({ method: 'SubmitAnswer', args: [quizId, questionId, optionId, submissionId] })
    return this.submitReply(questionId, optionId, submissionId)
  }

  on<E extends HubEventName>(event: E, handler: (payload: HubEvents[E]) => void) {
    const list = this.handlers.get(event) ?? []
    list.push(handler as (payload: never) => void)
    this.handlers.set(event, list)
  }

  onClose(handler: () => void) {
    this.closeHandlers.push(handler)
  }

  emit<E extends HubEventName>(event: E, payload: HubEvents[E]) {
    for (const handler of this.handlers.get(event) ?? []) {
      ;(handler as (p: HubEvents[E]) => void)(payload)
    }
  }

  drop() {
    this.connected = false
    for (const handler of this.closeHandlers) {
      handler()
    }
  }

  invoked(method: string) {
    return this.calls.filter((c) => c.method === method)
  }
}
