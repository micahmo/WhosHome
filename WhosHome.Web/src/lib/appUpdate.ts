/**
 * Notices when a newer build has been deployed while the app is open.
 *
 * Not through the service worker, which is the usual route and does not work here. `sw.js` is copied
 * into the build untouched and names no bundles, so it is byte identical between deploys, and a
 * browser only installs a new worker when that file's bytes change. `updatefound` would never fire.
 *
 * What does change on every build is the hashed script name in `index.html`. Comparing the one the
 * server is serving against the one this page is running answers the question directly, and needs
 * nothing from the server or the build.
 */

/** The bundle this page loaded. Vite writes exactly one module script tag into index.html. */
function runningBundle(): string | null {
  const script = document.querySelector<HTMLScriptElement>('script[type="module"][src]')
  return script === null ? null : new URL(script.src, window.location.origin).pathname
}

async function deployedBundle(): Promise<string | null> {
  try {
    const response = await fetch('/index.html', { cache: 'no-store' })
    if (!response.ok) {
      return null
    }

    const match = /<script[^>]+type="module"[^>]+src="([^"]+)"/.exec(await response.text())
    return match === null ? null : new URL(match[1], window.location.origin).pathname
  } catch {
    // Offline, or the server is mid-restart during a deploy. Either way the next check covers it,
    // and the service worker's cached copy would only ever match what we are already running.
    return null
  }
}

/**
 * Calls back once, when the served build stops matching the running one. Checks on returning to the
 * app as well as on a timer, because this app is built to be left open for days: the screen is held
 * awake so the board can be watched, and a page nobody navigates away from would otherwise sit on
 * an old version indefinitely.
 */
export function watchForUpdate(onReady: () => void, intervalMs = 300_000): void {
  const running = runningBundle()
  if (running === null) {
    return
  }

  // Which build was announced, rather than merely whether one was. Someone who dismisses the
  // prompt has said "not now" about that build, not "never tell me again", and on a page left open
  // for days a second deploy deserves to be mentioned as much as the first did.
  let announcedFor: string | null = null

  const check = async () => {
    if (document.visibilityState !== 'visible') {
      return
    }

    const deployed = await deployedBundle()
    if (deployed !== null && deployed !== running && deployed !== announcedFor) {
      announcedFor = deployed
      onReady()
    }
  }

  armed = check
  document.addEventListener('visibilitychange', () => void check())
  window.setInterval(() => void check(), intervalMs)
  void check()
}

let armed: (() => Promise<void>) | null = null

/**
 * Checks straight away rather than waiting for the timer.
 *
 * For callers that know something about the server has changed. A deploy takes the container down,
 * which drops every event stream, so a stream reconnecting is the earliest evidence available that
 * a new build might be serving — much earlier than any interval worth polling on.
 */
export function checkForUpdateNow(): void {
  void armed?.()
}
