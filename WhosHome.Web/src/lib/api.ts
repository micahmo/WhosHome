import type { PersonSummary, PresenceView, Session, SetupInfo, SetupLink } from './types'

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

// ---- Member session ----

/** Returns the signed-in person, or null when there is no valid session. */
export async function getSession(): Promise<Session | null> {
  return orNullOn401(() => request<Session>('/api/session'))
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

// ---- Admin mode ----

export async function isAdmin(): Promise<boolean> {
  return (await orNullOn401(() => request<{ admin: boolean }>('/api/admin/session'))) !== null
}

export function adminSignIn(token: string): Promise<void> {
  return request<void>('/api/admin/session', {
    method: 'POST',
    body: JSON.stringify({ token }),
  })
}

export function adminSignOut(): Promise<void> {
  return request<void>('/api/admin/session', { method: 'DELETE' })
}

// ---- Household management ----

export function listPeople(): Promise<PersonSummary[]> {
  return request<PersonSummary[]>('/api/people')
}

export function addPerson(name: string): Promise<PersonSummary> {
  return request<PersonSummary>('/api/people', {
    method: 'POST',
    body: JSON.stringify({ name }),
  })
}

export function createSetupLink(personId: number): Promise<SetupLink> {
  return request<SetupLink>(`/api/people/${personId}/code`, { method: 'POST' })
}

export function removePerson(personId: number): Promise<void> {
  return request<void>(`/api/people/${personId}`, { method: 'DELETE' })
}

// ---- The board, and the setup page ----

export function getPresence(): Promise<PresenceView[]> {
  return request<PresenceView[]>('/api/presence')
}

export function getSetup(token: string): Promise<SetupInfo> {
  return request<SetupInfo>(`/api/setup/${encodeURIComponent(token)}`)
}

async function orNullOn401<T>(call: () => Promise<T>): Promise<T | null> {
  try {
    return await call()
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }
    throw error
  }
}
