<script lang="ts">
  import { onMount } from 'svelte'

  /** Chromium fires this so a site can offer its own install button. Safari never does. */
  interface BeforeInstallPromptEvent extends Event {
    prompt(): Promise<void>
    userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
  }

  const dismissedKey = 'whoshome.install-dismissed'

  let installed = $state(true)
  let dismissed = $state(false)
  let deferredPrompt = $state<BeforeInstallPromptEvent | null>(null)
  let isIos = $state(false)

  // Shown only when the app is running in a browser tab. Once it has been added to the home
  // screen the display mode changes and this disappears for good.
  let visible = $derived(!installed && !dismissed)

  onMount(() => {
    installed =
      window.matchMedia('(display-mode: standalone)').matches ||
      // iOS predates the standard and reports installation its own way.
      (window.navigator as unknown as { standalone?: boolean }).standalone === true

    isIos = /iphone|ipad|ipod/i.test(window.navigator.userAgent)
    dismissed = localStorage.getItem(dismissedKey) === 'true'

    function capture(event: Event) {
      event.preventDefault()
      deferredPrompt = event as BeforeInstallPromptEvent
    }

    window.addEventListener('beforeinstallprompt', capture)
    return () => window.removeEventListener('beforeinstallprompt', capture)
  })

  async function install() {
    if (!deferredPrompt) {
      return
    }
    await deferredPrompt.prompt()
    await deferredPrompt.userChoice
    deferredPrompt = null
  }

  function dismiss() {
    dismissed = true
    localStorage.setItem(dismissedKey, 'true')
  }
</script>

{#if visible}
  <aside class="banner">
    <div class="text">
      <strong>Add to your home screen</strong>
      {#if isIos}
        <!-- iOS has no install API at all, so the best available option is telling the
             user exactly which buttons to press. -->
        <span>Tap Share, then "Add to Home Screen".</span>
      {:else}
        <span>Get an icon and a full screen app.</span>
      {/if}
    </div>

    {#if deferredPrompt}
      <button class="primary" onclick={install}>Install</button>
    {/if}
    <button class="dismiss" onclick={dismiss} aria-label="Dismiss">&times;</button>
  </aside>
{/if}

<style>
  .banner {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.75rem;
    margin-bottom: 1rem;
    border: 1px solid var(--line);
    border-radius: 0.6rem;
    background: var(--surface);
  }

  .text {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-width: 0;
    font-size: 0.85rem;
  }

  .text span {
    color: var(--muted);
  }

  .primary {
    border: none;
    border-radius: 0.5rem;
    padding: 0.5rem 0.9rem;
    background: var(--accent);
    color: var(--accent-text);
    font-weight: 600;
  }

  .dismiss {
    border: none;
    background: none;
    color: var(--muted);
    font-size: 1.4rem;
    line-height: 1;
    padding: 0 0.25rem;
  }
</style>
