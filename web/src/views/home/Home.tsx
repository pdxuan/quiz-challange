// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by views/home/Home.test.tsx (docs/ai-collab/s5.md).
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { api } from '../../api/client'
import { useUser } from '../login/RequireUser'
import styles from './Home.module.css'

export function Home() {
  const user = useUser()
  const navigate = useNavigate()
  const [quizId, setQuizId] = useState('')
  const id = quizId.trim().toUpperCase()

  function join(event: FormEvent) {
    event.preventDefault()
    if (id) {
      navigate(`/play/${encodeURIComponent(id)}`)
    }
  }

  async function logout() {
    try {
      await api.logout()
    } finally {
      navigate('/login', { replace: true })
    }
  }

  return (
    <section className={styles.panel}>
      <p className={styles.user}>
        Logged in as {user.username}{' '}
        <button type="button" onClick={logout}>
          Log out
        </button>
      </p>
      <h2>Join a quiz</h2>
      <form className={styles.form} onSubmit={join}>
        <label>
          Quiz ID
          <input value={quizId} onChange={(e) => setQuizId(e.target.value)} maxLength={10} autoFocus />
        </label>
        <button type="submit" disabled={!id}>
          Join
        </button>
      </form>
      <p>
        Or <Link to="/host">host a quiz</Link>.
      </p>
    </section>
  )
}
