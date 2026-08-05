import type { PresenceState } from './types'

/** Distance rounded to something a person would actually say out loud. */
export function formatDistance(meters: number | null): string {
  if (meters === null) {
    return ''
  }

  if (meters < 1000) {
    return `${Math.round(meters / 10) * 10} m`
  }

  const kilometers = meters / 1000
  return kilometers < 10 ? `${kilometers.toFixed(1)} km` : `${Math.round(kilometers)} km`
}

/** Coarse relative age. Precision past "a few minutes" is noise here. */
export function formatAge(seconds: number | null): string {
  if (seconds === null) {
    return 'never reported'
  }

  if (seconds < 90) {
    return 'just now'
  }

  const minutes = Math.round(seconds / 60)
  if (minutes < 60) {
    return `${minutes} min ago`
  }

  const hours = Math.round(minutes / 60)
  if (hours < 24) {
    return hours === 1 ? 'an hour ago' : `${hours} hours ago`
  }

  const days = Math.round(hours / 24)
  return days === 1 ? 'yesterday' : `${days} days ago`
}

export function stateLabel(state: PresenceState): string {
  switch (state) {
    case 'Home':
      return 'Home'
    case 'Nearby':
      return 'Nearby'
    case 'Away':
      return 'Away'
    default:
      return 'No reports yet'
  }
}
