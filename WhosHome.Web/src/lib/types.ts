/** Mirrors PresenceState on the server. */
export type PresenceState = 'Unknown' | 'Home' | 'Nearby' | 'Away'

/**
 * Mirrors PresenceView on the server. Deliberately carries no coordinates: there is nothing
 * here that could be used to draw a map, which is the whole point of the app.
 */
export interface PresenceView {
  personId: number
  name: string
  state: PresenceState
  distanceMeters: number | null
  lastReportedUtc: string | null
  /** Seconds since the last report, or null if there has never been one. */
  ageSeconds: number | null
  /** The state is still the last known one; this says to present it as history. */
  isStale: boolean
  batteryPercent: number | null
}

export interface Session {
  personId: number
  name: string
}

/** Whether this person hears about that person. Defaults to on for everyone but yourself. */
export interface NotificationPreference {
  personId: number
  name: string
  isSelf: boolean
  enabled: boolean
}

export interface PersonSummary {
  id: number
  name: string
  deviceId: string
  /** The live setup link, or nulls when there is none or it has expired. Returned with the
   * person so the admin page survives a refresh without minting a replacement. */
  code: string | null
  setupUrl: string | null
  expiresUtc: string | null
}

export interface SetupLink {
  code: string
  expiresUtc: string
  setupUrl: string
}

/** What the setup page shows someone. Fetched with an unguessable token, not a session. */
export interface SetupInfo {
  name: string
  code: string | null
  ingestUrl: string
  /** Ready-made org.traccar.client:// link that configures the app in one tap. */
  traccarUrl: string
  expiresUtc: string
}
