/**
 * Holds a screen wake lock for as long as the app is on screen.
 *
 * Bringing this up is the act of deciding to watch the board, so the screen dimming halfway through
 * is the annoying case and there is nothing to configure: closing the app is how you stop.
 *
 * The browser drops the lock whenever the page stops being visible and never restores it by itself,
 * so the visibility listener is the whole mechanism rather than a refinement. It also means this
 * cannot hold a phone awake in the background: switch away and the lock is gone.
 */
export function keepScreenAwake(): void {
  if (!('wakeLock' in navigator)) {
    return
  }

  let sentinel: WakeLockSentinel | null = null

  const acquire = async () => {
    // A request while hidden is rejected, so there is no point making one.
    if (document.visibilityState !== 'visible') {
      return
    }

    if (sentinel !== null && !sentinel.released) {
      return
    }

    try {
      sentinel = await navigator.wakeLock.request('screen')
    } catch {
      // Battery saver refuses, and so does a backgrounded page. Neither is worth surfacing: the
      // screen simply behaves as it normally would.
      sentinel = null
    }
  }

  document.addEventListener('visibilitychange', () => void acquire())
  void acquire()
}
