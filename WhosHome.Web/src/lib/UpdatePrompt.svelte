<script lang="ts">
  import { onMount } from 'svelte'
  import { watchForUpdate } from './appUpdate'

  let ready = $state(false)
  let applying = $state(false)

  onMount(() => {
    watchForUpdate(() => {
      ready = true
    })
  })

  function reload() {
    applying = true
    // The service worker fetches network first, so an ordinary reload picks up the new bundle. No
    // cache to clear and nothing to coordinate with the worker.
    window.location.reload()
  }
</script>

{#if ready}
  <!-- Polite rather than assertive: worth noticing, never worth interrupting someone mid-read. -->
  <div class="toast" role="status">
    <span>Who's Home has been updated.</span>
    <button onclick={reload} disabled={applying}>
      {applying ? 'Refreshing...' : 'Refresh'}
    </button>
    <!-- Because the alternative is a bar across the bottom of the board until you give in. -->
    <button class="dismiss" onclick={() => (ready = false)} aria-label="Dismiss">&times;</button>
  </div>
{/if}

<style>
  .toast {
    position: fixed;
    /* Anchored to both edges and centred with auto margins rather than left: 50% and a transform.
       A fixed element sizes itself against the space from its offsets to the edge, so left: 50%
       leaves it half the viewport to fit in, and the text wraps while the box still looks like it
       has room to spare. */
    left: 0;
    right: 0;
    width: fit-content;
    margin: 0 auto;
    bottom: calc(1rem + env(safe-area-inset-bottom));
    z-index: 10;

    display: flex;
    align-items: center;
    gap: 0.75rem;

    max-width: calc(100vw - 2rem);
    padding: 0.6rem 0.7rem 0.6rem 1rem;
    border: 1px solid var(--line);
    border-radius: 0.6rem;
    background: var(--surface);
    font-size: 0.85rem;
  }

  button {
    border: none;
    border-radius: 0.4rem;
    padding: 0.4rem 0.75rem;
    background: var(--accent);
    color: var(--accent-text);
    font-family: inherit;
    font-size: 0.85rem;
    font-weight: 600;
  }

  button:disabled {
    opacity: 0.5;
  }

  .dismiss {
    border: none;
    background: none;
    color: var(--muted);
    font-size: 1.4rem;
    line-height: 1;
    padding: 0 0.25rem;
    font-weight: 400;
  }
</style>
