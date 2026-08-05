import { mount } from 'svelte'
import './app.css'
import App from './App.svelte'

const app = mount(App, {
  target: document.getElementById('app')!,
})

// Registered unconditionally because push notifications cannot work without it, and it also
// keeps the shell available so the app can say "Offline" instead of failing to load.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('/sw.js')
  })
}

export default app
