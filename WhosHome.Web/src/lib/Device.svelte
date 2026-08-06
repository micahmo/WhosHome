<script lang="ts">
  import { onMount } from 'svelte'
  import { ApiError, getDeviceConfig } from './api'
  import type { DeviceConfig } from './types'
  import { formatAge, formatTimestamp, deservesTimestamp } from './format'

  let config = $state<DeviceConfig | null>(null)
  let starting = $state(true)
  let error = $state('')
  let signedOut = $state(false)

  // Nothing here can read what the app currently holds, so the page never claims an update is
  // needed. Applying settings again is harmless, and this is the only honest feedback available.
  let ageSeconds = $derived(
    config?.lastSeenUtc ? (Date.now() - new Date(config.lastSeenUtc).getTime()) / 1000 : null,
  )

  onMount(async () => {
    try {
      config = await getDeviceConfig()
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        signedOut = true
      } else {
        error = 'Could not load your settings.'
      }
    } finally {
      starting = false
    }
  })
</script>

<main>
  {#if starting}
    <p class="muted">Loading...</p>
  {:else if signedOut}
    <h1>Update your phone</h1>
    <p class="muted">
      Open Who's Home first and sign in, then come back here. This page only works for someone
      already on the board.
    </p>
    <a class="link" href="/">Go to the board</a>
  {:else if error}
    <h1>Update your phone</h1>
    <p class="error" role="alert">{error}</p>
  {:else if config}
    <header>
      <h1>Your phone</h1>
      <a class="link" href="/">Board</a>
    </header>

    <p class="muted">
      Hi {config.name}. Tap below to put the current recommended settings on this phone. It is safe
      to do at any time, and safe to do more than once.
    </p>

    <ol>
      <li>
        <h2>Apply settings</h2>
        <p class="muted">Say yes when Traccar Client asks to apply them.</p>
        <a class="button" href={config.traccarUrl}>Update Traccar Client</a>
        <details>
          <summary>Do it by hand instead</summary>
          <p class="muted small">Server URL</p>
          <code>{config.ingestUrl}</code>
        </details>
      </li>

      <li>
        <h2>Make sure tracking is on</h2>
        <!-- Applying settings does not start tracking, and an app update can switch it off without
             saying so, which is the whole reason this page exists. -->
        <p class="muted">
          Applying settings does not switch tracking on, and an app update can switch it off. This
          turns it back on.
        </p>
        <a class="button secondary" href={config.startUrl}>Start tracking</a>
      </li>
    </ol>

    <p class="status">
      {#if ageSeconds === null}
        Your phone has never reported.
      {:else}
        Last heard from {formatAge(ageSeconds)}{deservesTimestamp(ageSeconds)
          ? ` (at ${formatTimestamp(config.lastSeenUtc)})`
          : ''}.
      {/if}
    </p>
    <p class="muted small">
      Reload this page in a few minutes to see whether that changed.
    </p>
  {/if}
</main>

<style>
  main {
    max-width: 30rem;
    margin: 0 auto;
    padding: 1.25rem;
    padding-top: calc(1.25rem + env(safe-area-inset-top));
    padding-bottom: calc(1.25rem + env(safe-area-inset-bottom));
  }

  header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 0.75rem;
  }

  h1 {
    margin: 0 0 0.75rem;
    font-size: 1.4rem;
  }

  h2 {
    margin: 0 0 0.35rem;
    font-size: 1rem;
  }

  ol {
    margin: 1.5rem 0 0;
    padding-left: 1.4rem;
    display: flex;
    flex-direction: column;
    gap: 1.75rem;
  }

  li::marker {
    color: var(--muted);
  }

  .button {
    display: inline-block;
    margin-top: 0.5rem;
    padding: 0.6rem 1rem;
    border: none;
    border-radius: 0.5rem;
    background: var(--accent);
    color: var(--accent-text);
    font-weight: 600;
    font-size: 0.95rem;
    text-decoration: none;
  }

  .button.secondary {
    background: var(--surface);
    color: var(--text);
    border: 1px solid var(--line);
    font-weight: 500;
  }

  .link {
    color: var(--muted);
    font-size: 0.85rem;
    text-decoration: none;
  }

  details {
    margin-top: 0.6rem;
  }

  summary {
    color: var(--muted);
    font-size: 0.8rem;
  }

  code {
    display: block;
    margin-top: 0.2rem;
    font-size: 0.75rem;
    word-break: break-all;
  }

  .status {
    margin: 1.75rem 0 0;
    padding-top: 1.25rem;
    border-top: 1px solid var(--line);
    font-size: 0.9rem;
    font-variant-numeric: tabular-nums;
  }

  .muted {
    color: var(--muted);
  }

  .small {
    font-size: 0.8rem;
    margin: 0.35rem 0 0;
  }

  .error {
    color: var(--danger);
    font-size: 0.9rem;
  }
</style>
