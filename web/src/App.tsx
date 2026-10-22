import { Link, Route, Routes } from 'react-router'
import styles from './App.module.css'
import { Home } from './views/home/Home'
import { HostLive } from './views/host/HostLive'
import { HostSetup } from './views/host/HostSetup'
import { Login } from './views/login/Login'
import { RequireUser } from './views/login/RequireUser'
import { Play } from './views/play/Play'

export default function App() {
  return (
    <div className={styles.desk}>
      <header className={styles.header}>
        <h1 className={styles.brand}>
          <Link to="/">Vocabulary Quiz</Link>
        </h1>
      </header>
      <main className={styles.main}>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/" element={<RequireUser><Home /></RequireUser>} />
          <Route path="/host" element={<RequireUser><HostSetup /></RequireUser>} />
          <Route path="/host/:quizId" element={<RequireUser><HostLive /></RequireUser>} />
          <Route path="/play/:quizId" element={<RequireUser><Play /></RequireUser>} />
        </Routes>
      </main>
    </div>
  )
}
