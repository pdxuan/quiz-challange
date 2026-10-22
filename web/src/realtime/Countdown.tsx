// AI-assisted: Claude Code (Claude Opus 5.5), slice S5; reviewed by a reviewer agent and a human; verified by views/play/Play.test.tsx (skewed clock), mutations (docs/ai-collab/s5.md).
import { useEffect, useState } from 'react'
import type { QuestionView } from './contracts'
import styles from './Countdown.module.css'

/** A question with its local deadline, fixed when the question was received. */
export interface TimedQuestion extends QuestionView {
  deadline: number
}

/**
 * The time left comes from the server's own clock (`endsAt - serverNow`), added to the local
 * clock at receipt, so a client clock that is off does not change the countdown.
 */
export function timed(question: QuestionView): TimedQuestion {
  return { ...question, deadline: Date.now() + (Date.parse(question.endsAt) - Date.parse(question.serverNow)) }
}

function secondsLeft(deadline: number) {
  return Math.max(0, Math.ceil((deadline - Date.now()) / 1000))
}

export function Countdown({ deadline }: { deadline: number }) {
  const [seconds, setSeconds] = useState(() => secondsLeft(deadline))

  useEffect(() => {
    setSeconds(secondsLeft(deadline))
    const timer = setInterval(() => setSeconds(secondsLeft(deadline)), 250)
    return () => clearInterval(timer)
  }, [deadline])

  return (
    <p role="timer" aria-live="off" className={styles.timer}>
      {seconds} s
    </p>
  )
}
