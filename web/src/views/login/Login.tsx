// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by views/login/Login.test.tsx (docs/ai-collab/s5.md).
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { api, ApiError } from '../../api/client'
import styles from './Login.module.css'

export function Login() {
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.login(username, password)
      navigate('/', { replace: true })
    } catch (e) {
      setBusy(false)
      if (e instanceof ApiError && e.status === 401) {
        setError('Wrong username or password')
      } else if (e instanceof ApiError && e.status === 400) {
        setError('Enter a username and a password')
      } else {
        setError('Could not log in. Please try again.')
      }
    }
  }

  return (
    <section className={styles.panel}>
      <h2>Log in</h2>
      <form className={styles.form} onSubmit={submit}>
        <label>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
        </label>
        {error && (
          <p role="alert" className={styles.error}>
            {error}
          </p>
        )}
        <button type="submit" disabled={busy}>
          Log in
        </button>
      </form>
    </section>
  )
}
