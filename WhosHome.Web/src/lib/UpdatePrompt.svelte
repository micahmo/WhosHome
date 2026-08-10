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
    <span>A new version is ready.</span>
    <button onclick={reload} disabled={applying}>
      {applying ? 'Updating...' : 'Reload'}
    </button>
  </div>
{/if}

<style>
  .toast {
    position: fixed;
    left: 50%;
    transform: translateX(-50%);
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
</style>
