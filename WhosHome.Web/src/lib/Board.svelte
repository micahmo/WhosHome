<script lang="ts">
  import type { NotificationPreference, PresenceView } from './types'
  import { formatAge, formatDistance, formatTravel, stateLabel } from './format'

  let {
    people,
    preferences = [],
    onTogglePerson,
  }: {
    people: PresenceView[]
    /** Empty when notifications are off for this browser, which hides the bells entirely. */
    preferences?: NotificationPreference[]
    onTogglePerson?: (preference: NotificationPreference) => void
  } = $props()

  function preferenceFor(personId: number): NotificationPreference | undefined {
    return preferences.find((preference) => preference.personId === personId)
  }
</script>

<ul class="board">
  {#each people as person (person.personId)}
    {@const preference = preferenceFor(person.personId)}
    <li class="card" class:stale={person.isStale}>
      <div class="head">
        <span class="who">
          <span class="dot" data-state={person.state}></span>
          <span class="name">{person.name}</span>
        </span>

        {#if preference && onTogglePerson}
          <button
            class="bell"
            class:on={preference.enabled}
            onclick={() => onTogglePerson(preference)}
            aria-pressed={preference.enabled}
            title={preference.enabled
              ? `Stop notifying me about ${person.name}`
              : `Notify me about ${person.name}`}
          >
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <path
                d="M18 8a6 6 0 1 0-12 0c0 7-3 8-3 8h18s-3-1-3-8"
                fill={preference.enabled ? 'currentColor' : 'none'}
                stroke="currentColor"
                stroke-width="1.7"
                stroke-linejoin="round"
              />
              <path
                d="M13.7 20.5a2 2 0 0 1-3.4 0"
                fill="none"
                stroke="currentColor"
                stroke-width="1.7"
                stroke-linecap="round"
              />
            </svg>
          </button>
        {/if}
      </div>

      <p class="state">
        {stateLabel(person.state)}
        {#if person.state !== 'Home' && person.distanceMeters !== null}
          <span class="distance">{formatDistance(person.distanceMeters)}</span>
        {/if}
      </p>

      {#if person.travelSeconds !== null}
        <p class="travel">{formatTravel(person.travelSeconds)}</p>
      {/if}

      <p class="meta">
        {formatAge(person.ageSeconds)}
        {#if person.batteryPercent !== null}
          <span class="sep">&middot;</span>{Math.round(person.batteryPercent)}% battery
        {/if}
      </p>
    </li>
  {/each}
</ul>

<style>
  .board {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 0.7rem;
  }

  /* A card per person, so extra detail can be added later without the rows running together. */
  .card {
    padding: 0.85rem 0.95rem;
    border: 1px solid var(--line);
    border-radius: 0.7rem;
    background: var(--surface);
  }

  /* Stale entries are still shown, just visibly presented as history rather than fact. */
  .card.stale {
    opacity: 0.5;
  }

  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
  }

  .who {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    min-width: 0;
  }

  .dot {
    width: 0.7rem;
    height: 0.7rem;
    border-radius: 50%;
    flex-shrink: 0;
    background: var(--muted);
  }

  .dot[data-state='Home'] {
    background: var(--home);
  }

  .dot[data-state='Nearby'] {
    background: var(--nearby);
  }

  .dot[data-state='Away'] {
    background: var(--away);
  }

  .name {
    font-weight: 600;
    font-size: 1.05rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  /* Small and clearly bounded, because this sits on a screen meant for glancing at and a
     mis-tap would silently change someone's notifications. */
  .bell {
    flex-shrink: 0;
    width: 2.1rem;
    height: 2.1rem;
    padding: 0.35rem;
    border: 1px solid var(--line);
    border-radius: 0.5rem;
    background: var(--bg);
    color: var(--muted);
    display: grid;
    place-items: center;
  }

  .bell.on {
    color: var(--home);
    border-color: var(--home);
  }

  .bell svg {
    width: 100%;
    height: 100%;
  }

  .state {
    margin: 0.5rem 0 0;
    font-size: 0.95rem;
  }

  .distance {
    color: var(--muted);
  }

  .distance::before {
    content: '\00b7';
    margin: 0 0.35rem;
  }

  .travel {
    margin: 0.15rem 0 0;
    font-size: 0.9rem;
    font-weight: 500;
  }

  .meta {
    margin: 0.15rem 0 0;
    font-size: 0.8rem;
    color: var(--muted);
    font-variant-numeric: tabular-nums;
  }

  .sep {
    margin: 0 0.35rem;
  }
</style>
