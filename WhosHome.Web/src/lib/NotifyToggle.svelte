<script lang="ts">
  import { onMount } from 'svelte'
  import {
    getNotificationPreferences,
    getPushKey,
    setNotificationPreference,
    subscribeToPush,
    unsubscribeFromPush,
  } from './api'
  import type { NotificationPreference } from './types'

  let supported = $state(false)
  let enabled = $state(false)
  let blocked = $state(false)
  let busy = $state(false)
  let error = $state('')
  let preferences = $state<NotificationPreference[]>([])

  onMount(async () => {
    supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
    if (!supported) {
      return
    }

    blocked = Notification.permission === 'denied'

    const registration = await navigator.serviceWorker.ready
    enabled = (await registration.pushManager.getSubscription()) !== null

    if (enabled) {
      await loadPreferences()
    }
  })

  async function loadPreferences() {
    try {
      preferences = await getNotificationPreferences()
    } catch {
      // Not worth an error banner: the toggles simply do not appear.
    }
  }

  async function togglePerson(preference: NotificationPreference) {
    const next = !preference.enabled
    // Optimistic, because a checkbox that lags feels broken.
    preferences = preferences.map((candidate) =>
      candidate.personId === preference.personId ? { ...candidate, enabled: next } : candidate,
    )

    try {
      await setNotificationPreference(preference.personId, next)
    } catch {
      error = `Could not change the setting for ${preference.name}.`
      await loadPreferences()
    }
  }

  async function enable() {
    busy = true
    error = ''

    try {
      // Must be called from a user gesture or the prompt is suppressed, which is why this lives
      // behind a button rather than happening on load.
      const permission = await Notification.requestPermission()
      if (permission !== 'granted') {
        blocked = permission === 'denied'
        return
      }

      const registration = await navigator.serviceWorker.ready
      const subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: base64UrlToBytes(await getPushKey()),
      })

      const json = subscription.toJSON()
      await subscribeToPush(subscription.endpoint, json.keys!.p256dh, json.keys!.auth)
      enabled = true
      await loadPreferences()
    } catch {
      error = 'Could not turn on notifications.'
    } finally {
      busy = false
    }
  }

  async function disable() {
    busy = true
    error = ''

    try {
      const registration = await navigator.serviceWorker.ready
      const subscription = await registration.pushManager.getSubscription()
      if (subscription) {
        await unsubscribeFromPush(subscription.endpoint)
        await subscription.unsubscribe()
      }
      enabled = false
      preferences = []
    } catch {
      error = 'Could not turn off notifications.'
    } finally {
      busy = false
    }
  }

  /**
   * The subscribe call wants raw bytes, not the base64url string the server sends. Backed by an
   * explicit ArrayBuffer because BufferSource rejects the SharedArrayBuffer-capable default.
   */
  function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
    const padded = value.padEnd(value.length + ((4 - (value.length % 4)) % 4), '=')
    const binary = atob(padded.replace(/-/g, '+').replace(/_/g, '/'))
    const bytes = new Uint8Array(new ArrayBuffer(binary.length))
    for (let index = 0; index < binary.length; index += 1) {
      bytes[index] = binary.charCodeAt(index)
    }
    return bytes
  }
</script>

{#if supported}
  <section>
    {#if blocked}
      <span class="muted">Notifications are blocked in your browser settings.</span>
    {:else}
      <button onclick={enabled ? disable : enable} disabled={busy}>
        {enabled ? 'Turn off notifications' : 'Notify me when someone gets home'}
      </button>
    {/if}

    {#if enabled && preferences.length > 0}
      <p class="muted">Tell me about</p>
      <ul>
        {#each preferences as preference (preference.personId)}
          <li>
            <label>
              <input
                type="checkbox"
                checked={preference.enabled}
                onchange={() => togglePerson(preference)}
              />
              <span>{preference.name}{preference.isSelf ? ' (you)' : ''}</span>
            </label>
          </li>
        {/each}
      </ul>
    {/if}

    {#if error}<p class="error">{error}</p>{/if}
  </section>
{/if}

<style>
  section {
    margin-top: 1.5rem;
    padding-top: 1.25rem;
    border-top: 1px solid var(--line);
  }

  p {
    margin: 1rem 0 0.5rem;
    font-size: 0.8rem;
  }

  ul {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  label {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    padding: 0.3rem 0;
    font-size: 0.9rem;
    cursor: pointer;
  }

  input[type='checkbox'] {
    width: 1.1rem;
    height: 1.1rem;
  }

  button {
    border: 1px solid var(--line);
    background: var(--surface);
    color: inherit;
    border-radius: 0.5rem;
    padding: 0.55rem 0.9rem;
    font-family: inherit;
    font-size: 0.85rem;
  }

  button:disabled {
    opacity: 0.5;
  }

  .muted {
    color: var(--muted);
    font-size: 0.8rem;
  }

  .error {
    color: var(--away);
    font-size: 0.8rem;
    margin: 0.25rem 0 0;
  }
</style>
