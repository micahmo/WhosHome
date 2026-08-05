<script lang="ts">
  import { ApiError, signIn } from './api'
  import type { Session } from './types'

  let { onSignedIn }: { onSignedIn: (session: Session) => void } = $props()

  let code = $state('')
  let busy = $state(false)
  let error = $state('')

  async function submit(event: SubmitEvent) {
    event.preventDefault()
    if (busy) {
      return
    }

    busy = true
    error = ''

    try {
      onSignedIn(await signIn(code.trim()))
    } catch (cause) {
      error =
        cause instanceof ApiError && cause.status === 401
          ? 'That code is not valid. Codes expire, so you may need a fresh one.'
          : 'Could not sign in. Check your connection and try again.'
    } finally {
      busy = false
    }
  }
</script>

<form onsubmit={submit}>
  <h1>Who's Home</h1>
  <p class="hint">Enter the code you were given.</p>

  <!-- inputmode numeric brings up the number pad without rejecting a pasted code. -->
  <input
    bind:value={code}
    inputmode="numeric"
    autocomplete="one-time-code"
    placeholder="000000"
    aria-label="Sign-in code"
    disabled={busy}
  />

  {#if error}
    <p class="error" role="alert">{error}</p>
  {/if}

  <button type="submit" disabled={busy || code.trim().length === 0}>
    {busy ? 'Signing in...' : 'Sign in'}
  </button>
</form>

<style>
  form {
    display: flex;
    flex-direction: column;
    gap: 1rem;
    max-width: 20rem;
    margin: 3rem auto 0;
  }

  h1 {
    margin: 0;
    font-size: 1.6rem;
  }

  .hint {
    margin: 0;
    color: var(--muted);
  }

  input {
    font-size: 1.6rem;
    text-align: center;
    letter-spacing: 0.4em;
    padding: 0.7rem;
    border-radius: 0.6rem;
    border: 1px solid var(--line);
    background: var(--surface);
    color: inherit;
  }

  button {
    font-size: 1rem;
    padding: 0.8rem;
    border: none;
    border-radius: 0.6rem;
    background: var(--accent);
    color: var(--accent-text);
    font-weight: 600;
  }

  button:disabled {
    opacity: 0.5;
  }

  .error {
    margin: 0;
    color: var(--away);
    font-size: 0.9rem;
  }
</style>
