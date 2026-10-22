// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by views/host/Host.test.tsx (docs/ai-collab/s5.md).
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { api, ApiError, type QuizSetSummary } from '../../api/client'
import { errorMessage } from '../../realtime/hub'
import styles from './Host.module.css'

export function HostSetup() {
  const navigate = useNavigate()
  const [sets, setSets] = useState<QuizSetSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let live = true
    api.quizSets().then(
      (list) => live && setSets(list),
      () => live && setError('Could not load the quiz sets.'),
    )
    return () => {
      live = false
    }
  }, [])

  async function open(quizSetId: string) {
    setBusy(true)
    setError(null)
    try {
      const { quizId } = await api.createSession(quizSetId)
      navigate(`/host/${encodeURIComponent(quizId)}`)
    } catch (e) {
      setBusy(false)
      setError(e instanceof ApiError ? errorMessage(e.code) : 'Could not open a session.')
    }
  }

  return (
    <section className={styles.panel}>
      <h2>Host a quiz</h2>
      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
      {sets === null && !error && <p>Loading quiz sets...</p>}
      {sets?.length === 0 && <p>No quiz sets yet.</p>}
      <ul className={styles.sets}>
        {sets?.map((s) => (
          <li key={s.id}>
            <strong>{s.title}</strong>
            <span>
              {s.questionCount} questions, {s.timeLimitSec} s each
            </span>
            <button type="button" disabled={busy} onClick={() => open(s.id)}>
              Open session
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
