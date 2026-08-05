<script lang="ts">
  import { onMount } from 'svelte'
  import { getSetup } from './api'
  import type { SetupInfo } from './types'

  let { token }: { token: string } = $props()

  const appStore = 'https://apps.apple.com/us/app/traccar-client/id843156974'
  const playStore = 'https://play.google.com/store/apps/details?id=org.traccar.client'

  let info = $state<SetupInfo | null>(null)
  let error = $state('')
  let loading = $state(true)
  let isIos = $state(false)

  onMount(async () => {
    isIos = /iphone|ipad|ipod/i.test(window.navigator.userAgent)
    try {
      info = await getSetup(token)
    } catch {
      error = 'This setup link has expired or is not valid. Ask for a new one.'
    } finally {
      loading = false
    }
  })
</script>

<main>
  {#if loading}
    <p class="muted">Loading...</p>
  {:else if error || !info}
    <h1>Who's Home</h1>
    <p class="error">{error}</p>
  {:else}
    <h1>Hi {info.name}</h1>
    <p class="muted">Three steps, about two minutes.</p>

    <ol>
      <li>
        <h2>Install Traccar Client</h2>
        <p class="muted">
          This is the app that tells Who's Home how far away you are. It is the only app you
          need to install.
        </p>
        <a class="button" href={isIos ? appStore : playStore} target="_blank" rel="noreferrer">
          {isIos ? 'Get it on the App Store' : 'Get it on Google Play'}
        </a>
      </li>

      <li>
        <h2>Set it up</h2>
        <p class="muted">
          Come back here once it is installed and tap below. It fills in the settings for you,
          then turn tracking on with the switch at the top of the app.
        </p>
        <a class="button" href={info.traccarUrl}>Configure Traccar Client</a>
        <p class="muted small">
          When it asks for location, choose <strong>Always</strong>. Anything less and it stops
          reporting the moment you close the app. Also avoid swiping the app away, which stops
          it entirely on iOS.
        </p>
        <details>
          <summary>Do it by hand instead</summary>
          <p class="muted small">Server URL</p>
          <code>{info.ingestUrl}</code>
        </details>
      </li>

      <li>
        <h2>Sign in here</h2>
        {#if info.code}
          <p class="muted">Your code:</p>
          <p class="code">{info.code}</p>
        {:else}
          <p class="muted">Ask for a fresh setup link to get a code.</p>
        {/if}
        <p class="muted small">
          {#if isIos}
            Tap Share, then "Add to Home Screen". Open Who's Home from the new icon and enter
            the code there.
          {:else}
            Add Who's Home to your home screen from the browser menu, then open it and enter the
            code.
          {/if}
          Install it first, then enter the code, or you will end up signed in to the browser and
          not the app.
        </p>
      </li>
    </ol>

    <p class="muted small">
      Nobody sees where you are. The others only see how far you are from home, and nothing
      else.
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

  h1 {
    margin: 0 0 0.25rem;
    font-size: 1.5rem;
  }

  h2 {
    margin: 0 0 0.35rem;
    font-size: 1rem;
  }

  ol {
    margin: 1.5rem 0;
    padding-left: 1.2rem;
  }

  li {
    margin-bottom: 1.75rem;
  }

  .button {
    display: inline-block;
    margin: 0.5rem 0;
    padding: 0.7rem 1.1rem;
    border-radius: 0.5rem;
    background: var(--accent);
    color: var(--accent-text);
    font-weight: 600;
    text-decoration: none;
  }

  .code {
    font-size: 2rem;
    font-weight: 700;
    letter-spacing: 0.35em;
    margin: 0.25rem 0 0.5rem;
    font-variant-numeric: tabular-nums;
  }

  code {
    display: block;
    overflow-x: auto;
    white-space: nowrap;
    font-size: 0.8rem;
    padding: 0.5rem;
    border-radius: 0.4rem;
    background: var(--surface);
  }

  details {
    margin-top: 0.75rem;
  }

  summary {
    font-size: 0.85rem;
    color: var(--muted);
    cursor: pointer;
  }

  .muted {
    color: var(--muted);
  }

  .small {
    font-size: 0.8rem;
  }

  .error {
    color: var(--away);
  }
</style>
