<script lang="ts">
  import { onMount } from 'svelte'
  import {
    addPerson,
    adminSignIn,
    adminSignOut,
    createSetupLink,
    isAdmin,
    listPeople,
    removePerson,
  } from './api'
  import type { PersonSummary } from './types'

  let admin = $state(false)
  let starting = $state(true)
  let token = $state('')
  let error = $state('')
  let busy = $state(false)

  // The server is the only source of truth for live setup links. Keeping a copy in component
  // state meant a refresh lost it, and the only way to see a code again was to mint a new one,
  // which invalidated whatever had already been sent.
  let people = $state<PersonSummary[]>([])
  let newName = $state('')

  let copied = $state<number | null>(null)

  /** The setup page explains itself, so the message does not have to. */
  function inviteText(url: string): string {
    return `Join Who's Home: ${url}`
  }

  onMount(async () => {
    try {
      admin = await isAdmin()
      if (admin) {
        people = await listPeople()
      }
    } finally {
      starting = false
    }
  })

  async function enterAdmin(event: SubmitEvent) {
    event.preventDefault()
    busy = true
    error = ''
    try {
      await adminSignIn(token.trim())
      admin = true
      token = ''
      people = await listPeople()
    } catch {
      error = 'That token is not valid.'
    } finally {
      busy = false
    }
  }

  async function leaveAdmin() {
    await adminSignOut()
    admin = false
    people = []
  }

  async function add(event: SubmitEvent) {
    event.preventDefault()
    const name = newName.trim()
    if (!name || busy) {
      return
    }

    busy = true
    error = ''
    try {
      const person = await addPerson(name)
      newName = ''
      // Adding someone is only ever a prelude to onboarding them, so skip a click.
      await createSetupLink(person.id)
      people = await listPeople()
    } catch {
      error = 'Could not add that person.'
    } finally {
      busy = false
    }
  }

  async function newLink(person: PersonSummary) {
    // Replacing a live link invalidates the one already sent, so say so when there is one.
    if (
      person.setupUrl &&
      !confirm(`Replace ${person.name}'s setup link? The link and code you already sent will stop working.`)
    ) {
      return
    }

    busy = true
    error = ''
    try {
      await createSetupLink(person.id)
      people = await listPeople()
    } catch {
      error = 'Could not create a setup link.'
    } finally {
      busy = false
    }
  }

  async function remove(person: PersonSummary) {
    // Deleting takes their reports with it and cannot be undone, so ask first.
    if (!confirm(`Remove ${person.name}? Their device will stop reporting and their history is deleted.`)) {
      return
    }

    busy = true
    error = ''
    try {
      await removePerson(person.id)
      people = await listPeople()
    } catch {
      error = `Could not remove ${person.name}.`
    } finally {
      busy = false
    }
  }

  async function copy(person: PersonSummary, text: string) {
    try {
      await navigator.clipboard.writeText(text)
      copied = person.id
      setTimeout(() => (copied = null), 2000)
    } catch {
      // Clipboard access needs a focused, secure context; the message is on screen either way.
      error = 'Could not copy. Select the message and copy it manually.'
    }
  }
</script>

<main>
  {#if starting}
    <p class="muted">Loading...</p>
  {:else if !admin}
    <form class="gate" onsubmit={enterAdmin}>
      <h1>Admin</h1>
      <p class="muted">Paste the admin token to manage the household from this browser.</p>
      <input
        bind:value={token}
        type="password"
        autocomplete="off"
        placeholder="Admin token"
        aria-label="Admin token"
        disabled={busy}
      />
      {#if error}<p class="error" role="alert">{error}</p>{/if}
      <button type="submit" disabled={busy || token.trim().length === 0}>Enter admin mode</button>
      <p class="muted small">
        This browser becomes an admin. It does not become a person, and will not appear on the
        board.
      </p>
    </form>
  {:else}
    <header>
      <h1>Admin</h1>
      <button class="link" onclick={leaveAdmin}>Leave admin mode</button>
    </header>

    <form class="add" onsubmit={add}>
      <input bind:value={newName} placeholder="Name" aria-label="Name" disabled={busy} />
      <button type="submit" disabled={busy || newName.trim().length === 0}>Add</button>
    </form>

    {#if error}<p class="error" role="alert">{error}</p>{/if}

    <ul>
      {#each people as person (person.id)}
        <li>
          <div class="row">
            <span class="name">{person.name}</span>
            <span class="actions">
              <button class="secondary" onclick={() => newLink(person)} disabled={busy}>
                {person.setupUrl ? 'New link' : 'Setup link'}
              </button>
              <button class="danger" onclick={() => remove(person)} disabled={busy}>Remove</button>
            </span>
          </div>

          {#if person.setupUrl}
            {@const invite = inviteText(person.setupUrl)}
            <div class="link-box">
              <p class="muted small">
                Send this to {person.name}. The page shows their code
                {#if person.code}(<strong>{person.code}</strong>){/if} and the steps.
                {#if person.code}
                  Good for 24 hours.
                {:else}
                  The code has already been used, so make a new link if they need to sign in again.
                {/if}
              </p>

              <div class="url">
                <code>{invite}</code>
                <button class="secondary" onclick={() => copy(person, invite)}>
                  {copied === person.id ? 'Copied' : 'Copy'}
                </button>
              </div>
            </div>
          {/if}
        </li>
      {/each}
    </ul>

    {#if people.length === 0}
      <p class="muted">Nobody yet. Add someone above.</p>
    {/if}
  {/if}
</main>

<style>
  main {
    max-width: 34rem;
    margin: 0 auto;
    padding: 1.25rem;
    padding-top: calc(1.25rem + env(safe-area-inset-top));
    padding-bottom: calc(1.25rem + env(safe-area-inset-bottom));
  }

  header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
  }

  h1 {
    margin: 0 0 0.75rem;
    font-size: 1.4rem;
  }

  form {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }

  /* Centered in the column to match the sign-in screen, which is the other thing you can
     land on without a session. */
  form.gate {
    max-width: 22rem;
    margin: 3rem auto 0;
  }

  form.add {
    flex-direction: row;
    margin: 0.5rem 0 1.25rem;
  }

  input {
    flex: 1;
    font-size: 1rem;
    padding: 0.6rem;
    border-radius: 0.5rem;
    border: 1px solid var(--line);
    background: var(--surface);
    color: inherit;
  }

  button {
    padding: 0.6rem 1rem;
    border: none;
    border-radius: 0.5rem;
    background: var(--accent);
    color: var(--accent-text);
    font-weight: 600;
  }

  button:disabled {
    opacity: 0.5;
  }

  button.secondary {
    background: var(--surface);
    color: var(--text);
    border: 1px solid var(--line);
    font-weight: 500;
  }

  /* Reserve room for the longer "done" label so swapping the text does not resize the button
     and reflow the message sitting next to it. */
  .url button {
    min-width: 5.75rem;
  }

  .actions button.secondary {
    min-width: 7.5rem;
  }

  button.danger {
    background: none;
    color: var(--away);
    border: 1px solid var(--line);
    font-weight: 500;
  }

  button.link {
    background: none;
    color: var(--muted);
    font-size: 0.85rem;
    padding: 0;
  }

  .actions {
    display: flex;
    gap: 0.5rem;
    flex-shrink: 0;
  }

  ul {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  li {
    padding: 0.75rem 0;
    border-bottom: 1px solid var(--line);
  }

  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
  }

  .name {
    font-weight: 600;
  }

  .link-box {
    margin-top: 0.6rem;
    padding: 0.6rem;
    border-radius: 0.5rem;
    background: var(--surface);
  }

  .url {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-top: 0.4rem;
  }

  /* Wrapped rather than scrolled. A horizontal scrollbar sits on top of the text on Windows
     and hides the URL completely on hover, and this still has to be selectable by hand when
     the clipboard API is unavailable. */
  code {
    flex: 1;
    min-width: 0;
    font-size: 0.75rem;
    word-break: break-all;
  }

  .muted {
    color: var(--muted);
  }

  .small {
    font-size: 0.8rem;
    margin: 0;
  }

  .error {
    color: var(--away);
    font-size: 0.9rem;
  }
</style>
