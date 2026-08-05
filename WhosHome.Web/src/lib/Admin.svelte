<script lang="ts">
  import { onMount } from 'svelte'
  import { addPerson, adminSignIn, adminSignOut, createSetupLink, isAdmin, listPeople } from './api'
  import type { PersonSummary, SetupLink } from './types'

  let admin = $state(false)
  let starting = $state(true)
  let token = $state('')
  let error = $state('')
  let busy = $state(false)

  let people = $state<PersonSummary[]>([])
  let newName = $state('')

  /** Most recently created link, keyed by person, so several can be open at once. */
  let links = $state<Record<number, SetupLink>>({})
  let copied = $state<number | null>(null)

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
    links = {}
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
      people = await listPeople()
      // Adding someone is only ever a prelude to onboarding them, so skip a click.
      links = { ...links, [person.id]: await createSetupLink(person.id) }
    } catch {
      error = 'Could not add that person.'
    } finally {
      busy = false
    }
  }

  async function newLink(person: PersonSummary) {
    busy = true
    error = ''
    try {
      links = { ...links, [person.id]: await createSetupLink(person.id) }
    } catch {
      error = 'Could not create a setup link.'
    } finally {
      busy = false
    }
  }

  async function copy(person: PersonSummary, url: string) {
    try {
      await navigator.clipboard.writeText(url)
      copied = person.id
      setTimeout(() => (copied = null), 2000)
    } catch {
      // Clipboard access needs a secure context; the link is on screen to copy by hand.
      error = 'Could not copy. Select the link and copy it manually.'
    }
  }
</script>

<main>
  {#if starting}
    <p class="muted">Loading...</p>
  {:else if !admin}
    <form onsubmit={enterAdmin}>
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
            <button class="secondary" onclick={() => newLink(person)} disabled={busy}>
              {links[person.id] ? 'New link' : 'Setup link'}
            </button>
          </div>

          {#if links[person.id]}
            {@const link = links[person.id]}
            <div class="link-box">
              <p class="muted small">
                Send this to {person.name}. Code <strong>{link.code}</strong>. Expires in 24 hours,
                and opening it does not use it up; entering the code does.
              </p>
              <div class="url">
                <code>{link.setupUrl}</code>
                <button class="secondary" onclick={() => copy(person, link.setupUrl)}>
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
    max-width: 22rem;
  }

  form.add {
    flex-direction: row;
    max-width: none;
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

  button.link {
    background: none;
    color: var(--muted);
    font-size: 0.85rem;
    padding: 0;
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

  code {
    flex: 1;
    min-width: 0;
    overflow-x: auto;
    white-space: nowrap;
    font-size: 0.75rem;
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
