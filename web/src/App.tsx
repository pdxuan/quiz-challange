import { Route, Routes, useParams } from 'react-router'

// Route placeholders from the walking skeleton; S5 replaces each with its real view.
function Placeholder({ title }: { title: string }) {
  return <h2>{title}</h2>
}

function HostLive() {
  const { quizId } = useParams()
  return <Placeholder title={`Hosting ${quizId}`} />
}

function Play() {
  const { quizId } = useParams()
  return <Placeholder title={`Playing ${quizId}`} />
}

export default function App() {
  return (
    <main>
      <h1>Vocabulary Quiz</h1>
      <Routes>
        <Route path="/login" element={<Placeholder title="Log in" />} />
        <Route path="/" element={<Placeholder title="Join a quiz" />} />
        <Route path="/host" element={<Placeholder title="Host a quiz" />} />
        <Route path="/host/:quizId" element={<HostLive />} />
        <Route path="/play/:quizId" element={<Play />} />
      </Routes>
    </main>
  )
}
