export type Route =
  | { name: 'board' }
  | { name: 'admin' }
  | { name: 'device' }
  | { name: 'setup'; token: string }

/**
 * Four screens is not worth a routing library. The server sends index.html for any unmatched
 * path, so whatever the browser asked for arrives here intact.
 */
export function parseRoute(pathname: string): Route {
  if (pathname === '/admin' || pathname === '/admin/') {
    return { name: 'admin' }
  }

  if (pathname === '/device' || pathname === '/device/') {
    return { name: 'device' }
  }

  const setup = pathname.match(/^\/setup\/([A-Za-z0-9]+)\/?$/)
  if (setup) {
    return { name: 'setup', token: setup[1] }
  }

  return { name: 'board' }
}
