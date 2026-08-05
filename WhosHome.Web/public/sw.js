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
    self.registration.showNotification(payload.title || "Who's Home", {
      body: payload.body || '',
      // Replaces an earlier notification about the same person rather than stacking them up.
      tag: payload.tag,
      icon: '/icon-192.png',
      badge: '/icon-192.png',
    }),
  )
})

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
