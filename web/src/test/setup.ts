import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'

// Vitest runs without globals, so React Testing Library cannot clean up on its own.
afterEach(() => cleanup())
