// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by server PlayContracts.cs and the view tests (docs/ai-collab/s5.md).
// The hub contract (docs/TASK.md Contract; server: Features/Play/PlayContracts.cs, camelCase JSON).

export interface OptionView {
  optionId: string
  text: string
}

export interface QuestionView {
  questionId: string
  index: number
  total: number
  prompt: string
  options: OptionView[]
  endsAt: string
  serverNow: string
}

export interface BoardEntry {
  rank: number
  username: string
  totalPoints: number
  online: boolean
}

export interface JoinResult {
  username: string
  isOrganizer: boolean
  state: 'Lobby' | 'Running' | 'Finished'
  playerCount: number
  players: PlayerView[]
  currentQuestion: QuestionView | null
  canAnswerCurrent: boolean
  you: { totalPoints: number; rank: number }
  top10: BoardEntry[]
}

export interface PlayerView {
  username: string
  online: boolean
}

export interface WatchSnapshot {
  state: 'Lobby' | 'Running' | 'Finished'
  players: PlayerView[]
  currentQuestion: QuestionView | null
  answersCount: { answered: number; eligible: number } | null
  leaderboard: BoardEntry[]
}

export interface AnswerAccepted {
  questionId: string
  submissionId: string
}

export interface PlayerJoinedEvent {
  username: string
  playerCount: number
}

export interface PlayerOnlineChangedEvent {
  username: string
  online: boolean
}

export interface AnswersCountEvent {
  questionId: string
  answered: number
  eligible: number
}

export interface PlayerQuestionClosed {
  questionId: string
  correctOptionId: string
  yourOptionId: string | null
  correct: boolean
  points: number
  totalPoints: number
  rank: number
  top10: BoardEntry[]
}

export interface OrganizerQuestionClosed {
  questionId: string
  correctOptionId: string
  leaderboard: BoardEntry[]
}

export interface PlayerQuizFinished {
  totalPoints: number
  rank: number
  top10: BoardEntry[]
}

export interface OrganizerQuizFinished {
  leaderboard: BoardEntry[]
}

/** Server -> client events. Close and finish have a player payload and an organizer payload. */
export interface HubEvents {
  PlayerJoined: PlayerJoinedEvent
  PlayerOnlineChanged: PlayerOnlineChangedEvent
  QuestionStarted: QuestionView
  AnswersCount: AnswersCountEvent
  QuestionClosed: PlayerQuestionClosed | OrganizerQuestionClosed
  QuizFinished: PlayerQuizFinished | OrganizerQuizFinished
}

export type HubEventName = keyof HubEvents
