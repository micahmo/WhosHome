import type { PresenceView, Session } from './types'

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })

  if (!response.ok) {
    let message = response.statusText
    try {
      const body = await response.json()
      if (body?.error) {
        message = body.error
      }
    } catch {
      // Not every error response carries a JSON body; the status text will do.
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

/** Returns the signed-in person, or null when there is no valid session. */
export async function getSession(): Promise<Session | null> {
  try {
    return await request<Session>('/api/session')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }
    throw error
  }
}

export function signIn(code: string): Promise<Session> {
  return request<Session>('/api/session', {
    method: 'POST',
    body: JSON.stringify({ code }),
  })
}

export function signOut(): Promise<void> {
  return request<void>('/api/session', { method: 'DELETE' })
}

export function getPresence(): Promise<PresenceView[]> {
  return request<PresenceView[]>('/api/presence')
}
