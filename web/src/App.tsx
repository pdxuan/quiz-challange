import { Route, Routes } from 'react-router'
import { Home } from './views/home/Home'
import { HostLive } from './views/host/HostLive'
import { HostSetup } from './views/host/HostSetup'
import { Login } from './views/login/Login'
import { RequireUser } from './views/login/RequireUser'
import { Play } from './views/play/Play'

export default function App() {
  return (
    <main>
      <h1>Vocabulary Quiz</h1>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/" element={<RequireUser><Home /></RequireUser>} />
        <Route path="/host" element={<RequireUser><HostSetup /></RequireUser>} />
        <Route path="/host/:quizId" element={<RequireUser><HostLive /></RequireUser>} />
        <Route path="/play/:quizId" element={<RequireUser><Play /></RequireUser>} />
      </Routes>
    </main>
  )
}
