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
