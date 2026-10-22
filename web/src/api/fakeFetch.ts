// Test helper: a fake `fetch` that answers "METHOD /path" routes and records every call.

export interface FakeReply {
  status: number
  body?: unknown
}

export type FakeRoutes = Record<string, FakeReply | ((body: unknown) => FakeReply | Promise<FakeReply>)>

export interface FetchCall {
  method: string
  path: string
  body: unknown
}

export function fakeFetch(routes: FakeRoutes) {
  const calls: FetchCall[] = []
  const fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const path = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
    calls.push({ method, path, body })
    const route = routes[`${method} ${path}`]
    const reply = route === undefined ? { status: 404, body: { title: 'Not found' } } : typeof route === 'function' ? await route(body) : route
    return new Response(reply.body === undefined ? null : JSON.stringify(reply.body), {
      status: reply.status,
      headers: { 'Content-Type': 'application/json' },
    })
  }
  return { fetch, calls }
}
