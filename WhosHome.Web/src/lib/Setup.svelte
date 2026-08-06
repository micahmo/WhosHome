<script lang="ts">
  import { onMount } from 'svelte'
  import { getSetup } from './api'
  import type { SetupInfo } from './types'

  let { token }: { token: string } = $props()

  const appStore = 'https://apps.apple.com/us/app/traccar-client/id843156974'
  const playStore = 'https://play.google.com/store/apps/details?id=org.traccar.client'

  // Verified in the client source: the "action" host starts and stops tracking with no
  // confirmation dialog. The config link cannot switch tracking on, so this is the only way to
  // avoid telling people to go hunting for a toggle.
  const startTracking = 'org.traccar.client://action/start'

  /** Chromium fires this so a site can offer its own install button. Safari never does. */
  interface BeforeInstallPromptEvent extends Event {
    prompt(): Promise<void>
    userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
  }

  let info = $state<SetupInfo | null>(null)
  let error = $state('')
  let loading = $state(true)

  let isIos = $state(false)
  let isInAppBrowser = $state(false)
  let installed = $state(false)
  let deferredPrompt = $state<BeforeInstallPromptEvent | null>(null)
  let linkCopied = $state(false)
  let browserUrl = $state('')

  /**
   * Android honours intent:// from inside most web views, which is the only way to escape one
   * programmatically. Deliberately no package= so it hands off to whichever browser is the
   * default: pinning it to Chrome would simply fail for anyone who does not have Chrome.
   * There is no equivalent on iOS, and no browser anywhere exposes an API to trigger
   * installation directly.
   */
  function buildBrowserIntent(href: string): string {
    const url = new URL(href)
    const scheme = url.protocol.replace(':', '')
    return `intent://${url.host}${url.pathname}${url.search}#Intent;scheme=${scheme};action=android.intent.action.VIEW;end`
  }

  /**
   * In-app browsers cannot install a web app at all: no beforeinstallprompt, and no Add to Home
   * Screen in the menu. Detection is heuristic because there is no real API for it, so this
   * covers the common messaging apps rather than pretending to be exhaustive.
   */
  function detectInAppBrowser(userAgent: string): boolean {
    if (/; wv\)/.test(userAgent)) {
      return true
    }

    if (/FBAN|FBAV|FB_IAB|Instagram|Line\/|Snapchat|LinkedInApp|MicroMessenger|WhatsApp/i.test(userAgent)) {
      return true
    }

    // iOS web views run WebKit but omit the Safari token that real Safari includes.
    const iosDevice = /iPhone|iPad|iPod/.test(userAgent)
    const realBrowser = /Safari|CriOS|FxiOS|EdgiOS/.test(userAgent)
    return iosDevice && !realBrowser
  }

  onMount(() => {
    const userAgent = window.navigator.userAgent
    isIos = /iPhone|iPad|iPod/i.test(userAgent)
    isInAppBrowser = detectInAppBrowser(userAgent)
    installed =
      window.matchMedia('(display-mode: standalone)').matches ||
      (window.navigator as unknown as { standalone?: boolean }).standalone === true
    browserUrl = buildBrowserIntent(window.location.href)

    function capture(event: Event) {
      event.preventDefault()
      deferredPrompt = event as BeforeInstallPromptEvent
    }

    window.addEventListener('beforeinstallprompt', capture)
    void load()

    return () => window.removeEventListener('beforeinstallprompt', capture)
  })

  async function load() {
    try {
      info = await getSetup(token)
    } catch {
      error = 'This setup link has expired or is not valid. Ask for a new one.'
    } finally {
      loading = false
    }
  }

  async function install() {
    if (!deferredPrompt) {
      return
    }
    await deferredPrompt.prompt()
    await deferredPrompt.userChoice
    deferredPrompt = null
  }

  async function copyLink() {
    try {
      await navigator.clipboard.writeText(window.location.href)
      linkCopied = true
      setTimeout(() => (linkCopied = false), 2000)
    } catch {
      // Clipboard needs a focused, secure context. The address bar is the fallback.
    }
  }
</script>

<main>
  {#if loading}
    <p class="muted">Loading...</p>
  {:else if error || !info}
    <h1>Who's Home</h1>
    <p class="error">{error}</p>
  {:else}
    {#if isInAppBrowser}
      <aside class="notice">
        <strong>Open this page in your browser first</strong>
        <p>
          {#if isIos}
            Tap the Safari icon, or copy the link and paste it into Safari.
          {:else}
            Tap below, or copy the link and paste it into your browser.
          {/if}
          This browser cannot install apps.
        </p>
        {#if !isIos}
          <a class="escape" href={browserUrl}>Open in browser</a>
        {/if}
        <button onclick={copyLink}>{linkCopied ? 'Link copied' : 'Copy this link'}</button>
      </aside>
    {/if}

    <h1>Hi {info.name}</h1>
    <p class="muted">Four steps, a couple of minutes. Keep this page open as you go.</p>

    <ol>
      <li>
        <h2>Install Traccar Client</h2>
        <p class="muted">The only app you need. It reports how far you are from home.</p>
        <a class="button" href={isIos ? appStore : playStore} target="_blank" rel="noreferrer">
          {isIos ? 'Get it on the App Store' : 'Get it on Google Play'}
        </a>
      </li>

      <li>
        <h2>Point it at the right server</h2>
        <p class="muted">Come back here, tap below, and say yes when it asks to apply settings.</p>
        <a class="button" href={info.traccarUrl}>Configure Traccar Client</a>
        <details>
          <summary>Do it by hand instead</summary>
          <p class="muted small">Server URL</p>
          <code>{info.ingestUrl}</code>
        </details>
      </li>

      <li>
        <h2>Start tracking</h2>
        <p class="muted">Tap below, then allow location.</p>
        <a class="button" href={startTracking}>Start tracking</a>
        <p class="muted small">
          {#if isIos}
            Choose <strong>Allow While Using App</strong>, and say yes if iOS later offers
            <strong>Always Allow</strong>. Leave Traccar Client running rather than swiping it
            away.
          {:else}
            Choose <strong>Allow all the time</strong>. If you only see
            <strong>While using the app</strong>, accept it, then open Settings, Apps, Traccar
            Client, Permissions, Location and change it there.
          {/if}
        </p>
        <p class="muted small">Traccar Client should now show "Continuous tracking" switched on.</p>
      </li>

      <li>
        <h2>Add Who's Home to your home screen</h2>

        {#if installed}
          <p class="muted">Already installed. Enter your code below.</p>
        {:else if deferredPrompt}
          <p class="muted">One tap, then open it from the new icon.</p>
          <button class="button" onclick={install}>Install Who's Home</button>
        {:else if isIos}
          <p class="muted">
            Tap the Share button at the bottom of Safari, scroll down, and choose
            <strong>Add to Home Screen</strong>. Then open Who's Home from the new icon.
          </p>
        {:else if isInAppBrowser}
          <p class="muted">Open this page in your browser, then install it from there.</p>
          <a class="button" href={browserUrl}>Open in browser</a>
        {:else}
          <!-- A real browser that does not offer an install prompt, Firefox being the common
               case, so the menu is the only route and telling them to switch browsers would be
               both wrong and rude. -->
          <p class="muted">
            Open your browser menu and choose <strong>Install</strong> or
            <strong>Add to Home screen</strong>. Then open Who's Home from the new icon.
          </p>
        {/if}

        {#if info.code}
          <p class="muted small">Your code:</p>
          <p class="code">{info.code}</p>
          <p class="muted small">
            Enter it in the app you just added, not in this browser tab. It works once, so using
            it in the wrong place means asking for a new one.
          </p>
        {:else}
          <p class="muted">This code has already been used. Ask for a fresh setup link.</p>
        {/if}
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

  .notice {
    border: 1px solid var(--nearby);
    border-radius: 0.6rem;
    padding: 0.85rem;
    margin-bottom: 1.5rem;
    background: var(--surface);
  }

  .notice p {
    margin: 0.35rem 0 0.7rem;
    font-size: 0.85rem;
    color: var(--muted);
  }

  .notice button,
  .notice .escape {
    display: inline-block;
    border: 1px solid var(--line);
    background: none;
    color: inherit;
    border-radius: 0.5rem;
    padding: 0.5rem 0.9rem;
    font-family: inherit;
    font-size: 0.85rem;
    text-decoration: none;
    margin-right: 0.4rem;
  }

  .button {
    display: inline-block;
    margin: 0.5rem 0;
    padding: 0.7rem 1.1rem;
    border: none;
    border-radius: 0.5rem;
    background: var(--accent);
    color: var(--accent-text);
    font-weight: 600;
    font-size: 1rem;
    font-family: inherit;
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
    font-size: 0.8rem;
    word-break: break-all;
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
    color: var(--danger);
  }
</style>
