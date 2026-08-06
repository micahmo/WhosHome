import type {
  DeviceConfig,
  NotificationPreference,
  PersonSummary,
  PresenceView,
  Session,
  SetupInfo,
  SetupLink,
} from './types'

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

/** Always answers, so not being an admin is a false rather than a 401. */
export async function isAdmin(): Promise<boolean> {
  return (await request<{ admin: boolean }>('/api/admin/session')).admin
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

/** Every person's id, in the order they should appear. The server rejects a partial list. */
export function reorderPeople(ids: number[]): Promise<void> {
  return request<void>('/api/people/order', {
    method: 'PUT',
    body: JSON.stringify({ ids }),
  })
}

// ---- The board, and the setup page ----

export function getPresence(): Promise<PresenceView[]> {
  return request<PresenceView[]>('/api/presence')
}

export function getSetup(token: string): Promise<SetupInfo> {
  return request<SetupInfo>(`/api/setup/${encodeURIComponent(token)}`)
}

/** The signed-in member's own phone settings. Always their own; the session decides who that is. */
export function getDeviceConfig(): Promise<DeviceConfig> {
  return request<DeviceConfig>('/api/device/config')
}

// ---- Notifications ----

export async function getPushKey(): Promise<string> {
  return (await request<{ publicKey: string }>('/api/push/key')).publicKey
}

export function subscribeToPush(endpoint: string, p256dh: string, auth: string): Promise<void> {
  return request<void>('/api/push/subscribe', {
    method: 'POST',
    body: JSON.stringify({ endpoint, p256dh, auth }),
  })
}

export function unsubscribeFromPush(endpoint: string): Promise<void> {
  return request<void>('/api/push/subscribe', {
    method: 'DELETE',
    body: JSON.stringify({ endpoint }),
  })
}

export function getNotificationPreferences(): Promise<NotificationPreference[]> {
  return request<NotificationPreference[]>('/api/notifications')
}

export function setNotificationPreference(personId: number, enabled: boolean): Promise<void> {
  return request<void>(`/api/notifications/${personId}`, {
    method: 'PUT',
    body: JSON.stringify({ enabled }),
  })
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
