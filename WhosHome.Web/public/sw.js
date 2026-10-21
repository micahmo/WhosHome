// Who's Home service worker. Two jobs: receive push notifications, which is impossible without
// a service worker, and keep the app shell cached so opening the icon with no signal shows the
// app saying "Offline" rather than a browser error page.

const CACHE = 'whoshome-shell'

self.addEventListener('install', (event) => {
  event.waitUntil(self.skipWaiting())
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET') {
    return
  }

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) {
    return
  }

  // API responses are never cached. The board showing data that looks current but is hours old
  // would be worse than showing nothing, so those requests fail and the app reports it.
  if (url.pathname.startsWith('/api/') || url.pathname === '/ingest') {
    return
  }

  event.respondWith(networkFirst(request))
})

/**
 * Network first, cache as a fallback. Cache-first would be faster but reintroduces stale bundle
 * bugs, and this app is small enough that the round trip does not matter.
 */
async function networkFirst(request) {
  const cache = await caches.open(CACHE)

  try {
    const response = await fetch(request)
    if (response && response.ok) {
      cache.put(request, response.clone())
    }
    return response
  } catch (networkError) {
    const cached = await cache.match(request)
    if (cached) {
      return cached
    }

    // A navigation to a route we have never cached still needs the app shell, since the client
    // router works out what to render from the path.
    if (request.mode === 'navigate') {
      const shell = (await cache.match('/index.html')) || (await cache.match('/'))
      if (shell) {
        return shell
      }
    }

    return new Response('Offline', {
      status: 503,
      headers: { 'Content-Type': 'text/plain' },
    })
  }
}

self.addEventListener('push', (event) => {
  let payload = {}
  try {
    payload = event.data ? event.data.json() : {}
  } catch (parseError) {
    payload = {}
  }

  event.waitUntil(
    Promise.all([
      self.registration.showNotification(payload.title || "Who's Home", {
        body: bodyFor(payload),
        // Replaces an earlier notification about the same person rather than stacking them up.
        tag: payload.tag,
        icon: '/icon-192.png',
        badge: '/icon-192.png',
      }),
      // Tell the server a living install received this. A push service keeps accepting messages for
      // an endpoint whose app was uninstalled or reinstalled, so only a running worker acknowledging
      // proves the subscription is still attached to something. Best effort: losing an ack is
      // harmless and must never cost the notification.
      acknowledge(),
    ]),
  )
})

// A phone holds a background push until it wakes, so a notification can surface hours after the
// event. This runs at the moment it is shown, so it knows how stale it is: a fresh one keeps the
// natural "Just left home", a late one is dated ("Left home at 2:14 PM") so it never claims
// something happened just now when it happened this morning. The clock-time stamp Android shows is
// when the notification was posted, which is correct the other way round, so the two cases together
// are always right. Two minutes of slack absorbs the skew between this phone's clock and the one
// that reported.
const FRESH_MS = 2 * 60 * 1000

function bodyFor(payload) {
  const eventMs = Number(payload.eventUtc)
  if (payload.lateBody && eventMs > 0 && Date.now() - eventMs > FRESH_MS) {
    const time = formatTime(eventMs)
    if (time) {
      return `${payload.lateBody} at ${time}`
    }
  }
  return payload.body || ''
}

function formatTime(ms) {
  try {
    return new Date(ms).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  } catch (formatError) {
    return ''
  }
}

async function acknowledge() {
  try {
    const subscription = await self.registration.pushManager.getSubscription()
    if (!subscription) {
      return
    }
    await fetch('/api/push/ack', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ endpoint: subscription.endpoint }),
    })
  } catch (ackError) {
    // Recording only, so a failed acknowledgement changes nothing the user sees.
  }
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      for (const client of windows) {
        if (new URL(client.url).origin === self.location.origin) {
          return client.focus()
        }
      }
      return self.clients.openWindow('/')
    })(),
  )
})
