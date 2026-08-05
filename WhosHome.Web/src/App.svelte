<script lang="ts">
  import { onMount } from 'svelte'
  import { ApiError, getPresence, getSession, signOut } from './lib/api'
  import type { PresenceView, Session } from './lib/types'
  import Board from './lib/Board.svelte'
  import Login from './lib/Login.svelte'
  import InstallPrompt from './lib/InstallPrompt.svelte'

  const refreshIntervalMs = 30_000

  let session = $state<Session | null>(null)
  let people = $state<PresenceView[]>([])
  let starting = $state(true)
  let error = $state('')

  onMount(() => {
    void start()

    // Refresh on a timer, and again on focus, because iOS reloads a home screen app from
    // scratch after eviction and a stale board is the most likely thing to be looking at.
    const timer = setInterval(refresh, refreshIntervalMs)
    window.addEventListener('focus', refresh)

    return () => {
      clearInterval(timer)
      window.removeEventListener('focus', refresh)
    }
  })

  async function start() {
    try {
      session = await getSession()
      await refresh()
    } finally {
      starting = false
    }
  }

  async function refresh() {
    if (!session) {
      return
    }

    try {
      people = await getPresence()
      error = ''
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        session = null
        return
      }
      error = 'Could not reach the server.'
    }
  }

  async function onSignedIn(next: Session) {
    session = next
    await refresh()
  }

  async function leave() {
    await signOut()
    session = null
    people = []
  }
</script>

<main>
  {#if starting}
    <p class="muted">Loading...</p>
  {:else if !session}
    <Login {onSignedIn} />
  {:else}
    <InstallPrompt />

    <header>
      <h1>Who's Home</h1>
      <button class="link" onclick={leave}>Sign out</button>
    </header>

    {#if error}
      <p class="error" role="alert">{error}</p>
    {/if}

    <Board {people} />
  {/if}
</main>

<style>
  main {
    max-width: 30rem;
    margin: 0 auto;
    padding: 1.25rem;
    /* Respect the notch and the home indicator, or it looks broken when installed. */
    padding-top: calc(1.25rem + env(safe-area-inset-top));
    padding-bottom: calc(1.25rem + env(safe-area-inset-bottom));
  }

  header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    margin-bottom: 0.5rem;
  }

  h1 {
    margin: 0;
    font-size: 1.4rem;
  }

  .link {
    border: none;
    background: none;
    color: var(--muted);
    font-size: 0.85rem;
    padding: 0;
  }

  .muted {
    color: var(--muted);
  }

  .error {
    color: var(--away);
    font-size: 0.9rem;
  }
</style>
