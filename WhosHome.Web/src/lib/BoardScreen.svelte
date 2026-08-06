<script lang="ts">
  import { onMount } from 'svelte'
  import {
    ApiError,
    getNotificationPreferences,
    getPresence,
    getSession,
    isAdmin,
    setNotificationPreference,
    signOut,
  } from './api'
  import type { NotificationPreference, PresenceView, Session } from './types'
  import Board from './Board.svelte'
  import Login from './Login.svelte'
  import InstallPrompt from './InstallPrompt.svelte'
  import NotifyToggle from './NotifyToggle.svelte'

  const refreshIntervalMs = 30_000

  let session = $state<Session | null>(null)
  let admin = $state(false)
  let people = $state<PresenceView[]>([])
  let starting = $state(true)
  let error = $state('')

  // Held here rather than in NotifyToggle because the bells live on the board cards.
  let preferences = $state<NotificationPreference[]>([])

  // Preferences have to be refetched alongside the board, not once when push is switched on.
  // Someone added after that first fetch gets a card but no bell, which reads as their bell
  // being missing rather than as this list being out of date.
  let pushEnabled = $state(false)

  // Refreshing mid-toggle would overwrite the optimistic value with the pre-toggle one from the
  // server and flip the bell back under the person's finger.
  let togglesInFlight = 0

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
      // Asked even when signed in, because being a member and being an admin are independent.
      // Skipping it when a session exists is what left the admin page unreachable from the board
      // on the one browser most likely to be both.
      admin = await isAdmin()
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
      // Distinguish "you have no signal" from "the server is unreachable", because they call for
      // different reactions and looking identical is what makes an app feel broken.
      error = navigator.onLine ? 'Could not reach the server.' : 'Offline'
    }

    if (pushEnabled && togglesInFlight === 0) {
      await loadPreferences()
    }
  }

  async function loadPreferences() {
    try {
      preferences = await getNotificationPreferences()
    } catch {
      // No banner for this: the bells simply do not appear.
      preferences = []
    }
  }

  async function onPushEnabledChange(enabled: boolean) {
    pushEnabled = enabled
    if (!enabled) {
      preferences = []
      return
    }

    await loadPreferences()
  }

  async function togglePerson(preference: NotificationPreference) {
    const next = !preference.enabled
    // Optimistic, because a control that lags feels broken.
    preferences = preferences.map((candidate) =>
      candidate.personId === preference.personId ? { ...candidate, enabled: next } : candidate,
    )

    togglesInFlight++
    try {
      await setNotificationPreference(preference.personId, next)
    } catch {
      error = `Could not change notifications for ${preference.name}.`
      await loadPreferences()
    } finally {
      togglesInFlight--
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
    preferences = []
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
      <span class="header-links">
        <!-- An installed app has no address bar and no pull to refresh, so without this the only
             way to force an update is to close and reopen it. A full reload rather than just
             re-fetching the board, because that is what "refresh" means to whoever taps it, and
             because it also picks up a new build after the server has been updated. -->
        <button
          class="icon"
          aria-label="Refresh"
          title="Refresh"
          onclick={() => location.reload()}
        >
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path
              d="M20 12a8 8 0 1 1-2.34-5.66M20 4v3.5h-3.5"
              fill="none"
              stroke="currentColor"
              stroke-width="1.9"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>

        {#if session}
          <!-- Only for a member: an admin browsing the board has no phone on it to configure. -->
          <a class="link" href="/device">My phone</a>
        {/if}
        {#if admin}
          <a class="link" href="/admin">Admin</a>
        {/if}
        {#if session}
          <button class="link" onclick={leave}>Sign out</button>
        {/if}
      </span>
    </header>

    {#if !session}
      <p class="muted small">Viewing as admin. This browser is not on the board.</p>
    {/if}

    {#if error}
      <p class="error" role="alert">{error}</p>
    {/if}

    <Board {people} {preferences} onTogglePerson={session ? togglePerson : undefined} />

    {#if session}
      <NotifyToggle onEnabledChange={onPushEnabledChange} />
    {/if}
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

  /* Wraps rather than squeezing the title. A browser that is both a member and an admin carries four
     controls here, which is enough to break "Who's Home" across two lines on a phone. */
  header {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: 0.75rem;
    margin-bottom: 0.75rem;
  }

  h1 {
    white-space: nowrap;
  }

  .header-links {
    display: flex;
    align-items: center;
    gap: 0.9rem;
    flex-shrink: 0;
    /* Keeps them on the right when they wrap onto a line of their own, where space-between would
       otherwise leave a single item hard against the left edge. */
    margin-left: auto;
  }

  /* Sized for a thumb rather than to match the text links, since it is the one control here that
     gets tapped repeatedly. */
  .icon {
    width: 1.9rem;
    height: 1.9rem;
    padding: 0.25rem;
    border: none;
    background: none;
    color: var(--muted);
    display: grid;
    place-items: center;
  }

  .icon svg {
    width: 100%;
    height: 100%;
  }

  .icon:active {
    color: var(--text);
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
