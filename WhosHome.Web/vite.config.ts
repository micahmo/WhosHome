import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

// In development the Svelte dev server and the .NET server run side by side, so calls to the
// API are proxied across. In production ASP.NET Core serves the built files and the API from
// the same origin, and none of this applies.
const apiServer = 'http://localhost:5199'

export default defineConfig({
  plugins: [svelte()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': apiServer,
      '/ingest': apiServer,
    },
  },
})
