// REST client for the server (docs/TASK.md Contract). Same origin: the auth cookie goes along.

export interface User {
  userId: string
  username: string
}

export interface QuizSetSummary {
  id: string
  title: string
  questionCount: number
  timeLimitSec: number
}

/** A failed request. `code` is the ProblemDetails title (e.g. "NoPlayers"). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string

  constructor(status: number, code: string) {
    super(code)
    this.status = status
    this.code = code
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    let code = response.statusText || `HTTP ${response.status}`
    try {
      const problem = (await response.json()) as { title?: string }
      code = problem.title ?? code
    } catch {
      // not a ProblemDetails body: keep the status text
    }
    throw new ApiError(response.status, code)
  }
  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

export const api = {
  login: (username: string, password: string) => request<User>('POST', '/api/auth/login', { username, password }),
  logout: () => request<void>('POST', '/api/auth/logout'),
  me: () => request<User>('GET', '/api/auth/me'),
  quizSets: () => request<QuizSetSummary[]>('GET', '/api/quiz-sets'),
  createSession: (quizSetId: string) => request<{ quizId: string }>('POST', '/api/sessions', { quizSetId }),
  startSession: (quizId: string) =>
    request<{ state: string }>('POST', `/api/sessions/${encodeURIComponent(quizId)}/start`),
}
