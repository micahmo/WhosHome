<script lang="ts">
  import type { NotificationPreference, PresenceView } from './types'
  import {
    deservesTimestamp,
    formatAge,
    formatDistance,
    formatDwell,
    formatTimestamp,
    formatTravel,
    stateLabel,
  } from './format'

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
    {@const moving = person.isMoving && !person.isStale}
    <!-- Includes stale people. Where they settled, and when, stays true whatever happened after we
         stopped hearing from them; only the running duration would be a claim we cannot support. -->
    {@const settled = person.stationarySinceUtc !== null && !moving}
    <!-- Driving distance when routing answered, straight line otherwise. Nobody describes how far
         away they are as the crow flies, and the fallback keeps a number on the card either way. -->
    {@const shownDistance = person.travelMeters ?? person.distanceMeters}
    {@const dwell =
      formatDwell(person.stationarySeconds) +
      (deservesTimestamp(person.stationarySeconds)
        ? ` (since ${formatTimestamp(person.stationarySinceUtc)})`
        : '')}
    <!-- The arrival time alone once a phone goes quiet. "Stopped for four hours" asserts they are
         still there; "arrived at noon" only asserts when they got there, which we do know. -->
    {@const arrived = formatTimestamp(person.stationarySinceUtc)}
    {@const age =
      formatAge(person.ageSeconds) +
      (deservesTimestamp(person.ageSeconds)
        ? ` (at ${formatTimestamp(person.lastSeenUtc)})`
        : '')}
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
        {#if person.state !== 'Home' && shownDistance !== null}
          <span class="distance">{formatDistance(shownDistance)}</span>
        {/if}
        <!-- Only at home do "been home this long" and "been in one spot this long" mean the same
             thing, so only here can the duration hang off the state without misleading. -->
        {#if settled && person.state === 'Home'}
          <span class="distance">{person.isStale ? `since ${arrived}` : `for ${dwell}`}</span>
        {/if}
      </p>

      {#if moving || (settled && person.state !== 'Home') || person.travelSeconds !== null}
        <p class="travel">
          {#if moving}
            <span class="moving">On the move</span>
          {:else if settled && person.state !== 'Home'}
            <span class="muted">
              {person.isStale ? `Arrived ${arrived}` : `Stopped for ${dwell}`}
            </span>
          {/if}
          {#if person.travelSeconds !== null}
            {#if moving || (settled && person.state !== 'Home')}<span class="sep">&middot;</span>{/if}
            {formatTravel(person.travelSeconds)}
          {/if}
        </p>
      {/if}

      <p class="meta">
        {age}
        <!-- Still said in words rather than left to styling, because a card that only looked
             different would read as "switched off". But it follows the time and shares its colour:
             iPhones cannot check in while stationary, so this is the ordinary state of things for
             half the household rather than a fault worth colouring like one. -->
        {#if person.isStale && person.ageSeconds !== null}
          <span class="sep">&middot;</span>not checking in
        {/if}
        <!-- Dropped once a phone goes quiet. Everything else on the card was true when it was
             measured and stays true until contradicted, but a battery only ever falls, so an old
             reading is not stale information, it is wrong information. -->
        {#if person.batteryPercent !== null && !person.isStale}
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
    --state-color: var(--muted);
    width: 0.7rem;
    height: 0.7rem;
    border-radius: 50%;
    flex-shrink: 0;
    background: var(--state-color);
  }

  .dot[data-state='Home'] {
    --state-color: var(--home);
  }

  .dot[data-state='Nearby'] {
    --state-color: var(--nearby);
  }

  .dot[data-state='Away'] {
    --state-color: var(--away);
  }

  /* A stale card stays at full brightness. Dimming it looks like the person has been disabled, when
     all that has happened is that their phone stopped talking to us. The dot keeps its colour but
     goes hollow, which reads as "this was true a while ago". */
  .card.stale .dot {
    background: transparent;
    box-shadow: inset 0 0 0 0.14rem var(--state-color);
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

  .moving {
    color: var(--nearby);
  }

  .muted {
    color: var(--muted);
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
