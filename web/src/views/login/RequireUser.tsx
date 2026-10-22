import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { Navigate } from 'react-router'
import { api, ApiError, type User } from '../../api/client'

const UserContext = createContext<User | null>(null)

/** The logged-in user; only valid inside RequireUser. */
export function useUser(): User {
  const user = useContext(UserContext)
  if (user === null) {
    throw new Error('useUser must be used inside RequireUser')
  }
  return user
}

type AuthState = { status: 'loading' } | { status: 'in'; user: User } | { status: 'out' } | { status: 'error' }

/** Renders its children for a logged-in user (GET /api/auth/me) and sends anyone else to /login. */
export function RequireUser({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState>({ status: 'loading' })

  useEffect(() => {
    let live = true
    api.me().then(
      (user) => live && setAuth({ status: 'in', user }),
      (error: unknown) => live && setAuth(error instanceof ApiError && error.status === 401 ? { status: 'out' } : { status: 'error' }),
    )
    return () => {
      live = false
    }
  }, [])

  switch (auth.status) {
    case 'loading':
      return <p>Loading...</p>
    case 'out':
      return <Navigate to="/login" replace />
    case 'error':
      return <p role="alert">Could not reach the server. Please reload the page.</p>
    case 'in':
      return <UserContext value={auth.user}>{children}</UserContext>
  }
}
