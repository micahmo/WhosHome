import type { PresenceState } from './types'

const metresPerMile = 1609.344
const metresPerFoot = 0.3048

/**
 * Distance in the units the household actually thinks in. Feet close to home because "0.05 miles"
 * means nothing to anyone, miles beyond that.
 */
export function formatDistance(meters: number | null): string {
  if (meters === null) {
    return ''
  }

  const miles = meters / metresPerMile
  if (miles < 0.1) {
    const feet = Math.round(meters / metresPerFoot / 10) * 10
    return `${feet} ft`
  }

  return miles < 10 ? `${miles.toFixed(1)} mi` : `${Math.round(miles)} mi`
}

/** Driving time home. Rounded generously, because a routing estimate is not a promise. */
export function formatTravel(seconds: number | null): string {
  if (seconds === null) {
    return ''
  }

  const minutes = Math.round(seconds / 60)
  if (minutes < 1) {
    return 'under a minute away'
  }

  if (minutes < 60) {
    return `${minutes} min away`
  }

  const hours = Math.floor(minutes / 60)
  const remainder = minutes % 60
  return remainder === 0 ? `${hours} hr away` : `${hours} hr ${remainder} min away`
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
