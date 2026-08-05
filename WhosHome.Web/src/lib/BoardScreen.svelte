<script lang="ts">
  import { onMount } from 'svelte'
  import { ApiError, getPresence, getSession, isAdmin, signOut } from './api'
  import type { PresenceView, Session } from './types'
  import Board from './Board.svelte'
  import Login from './Login.svelte'
  import InstallPrompt from './InstallPrompt.svelte'

  const refreshIntervalMs = 30_000

  let session = $state<Session | null>(null)
  let admin = $state(false)
  let people = $state<PresenceView[]>([])
  let starting = $state(true)
  let error = $state('')

  // Admins can read the board without being a person, so the machine used for provisioning
  // does not have to register itself as a household member just to look.
  let canView = $derived(session !== null || admin)

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
      if (!session) {
        admin = await isAdmin()
      }
      await refresh()
    } finally {
      starting = false
    }
  }

  async function refresh() {
    if (!canView) {
      return
    }

    try {
      people = await getPresence()
      error = ''
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        session = null
        admin = false
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
  {:else if !canView}
    <Login {onSignedIn} />
  {:else}
    {#if session}
      <InstallPrompt />
    {/if}

    <header>
      <h1>Who's Home</h1>
      {#if session}
        <button class="link" onclick={leave}>Sign out</button>
      {:else}
        <a class="link" href="/admin">Admin</a>
      {/if}
    </header>

    {#if !session}
      <p class="muted small">Viewing as admin. This browser is not on the board.</p>
    {/if}

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
    text-decoration: none;
  }

  .muted {
    color: var(--muted);
  }

  .small {
    font-size: 0.8rem;
    margin: 0 0 0.5rem;
  }

  .error {
    color: var(--away);
    font-size: 0.9rem;
  }
</style>
