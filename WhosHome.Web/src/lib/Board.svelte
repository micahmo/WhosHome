<script lang="ts">
  import type { PresenceView } from './types'
  import { formatAge, formatDistance, stateLabel } from './format'

  let { people }: { people: PresenceView[] } = $props()
</script>

<ul class="board">
  {#each people as person (person.personId)}
    <li class="row" class:stale={person.isStale}>
      <span class="dot" data-state={person.state}></span>

      <span class="who">
        <span class="name">{person.name}</span>
        <span class="age">{formatAge(person.ageSeconds)}</span>
      </span>

      <span class="where">
        <span class="state">{stateLabel(person.state)}</span>
        {#if person.state !== 'Home' && person.distanceMeters !== null}
          <span class="distance">{formatDistance(person.distanceMeters)}</span>
        {/if}
      </span>
    </li>
  {/each}
</ul>

<style>
  .board {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .row {
    display: grid;
    grid-template-columns: auto 1fr auto;
    align-items: center;
    gap: 0.9rem;
    padding: 0.9rem 0.25rem;
    border-bottom: 1px solid var(--line);
  }

  .row:last-child {
    border-bottom: none;
  }

  /* Stale entries are still shown, just visibly presented as history rather than fact. */
  .row.stale {
    opacity: 0.45;
  }

  .dot {
    width: 0.7rem;
    height: 0.7rem;
    border-radius: 50%;
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

  .who,
  .where {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .where {
    text-align: right;
  }

  .name {
    font-weight: 600;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .state {
    font-variant-numeric: tabular-nums;
  }

  .age,
  .distance {
    font-size: 0.8rem;
    color: var(--muted);
    font-variant-numeric: tabular-nums;
  }
</style>
