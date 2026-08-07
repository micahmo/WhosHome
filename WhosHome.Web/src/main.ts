import { mount } from 'svelte'
import './app.css'
import App from './App.svelte'
import { keepScreenAwake } from './lib/keepAwake'

const app = mount(App, {
  target: document.getElementById('app')!,
})

// Opening this app is the act of choosing to watch it, so the screen stays on while it is on screen
// and goes back to normal the moment it is not.
keepScreenAwake()

// Registered unconditionally because push notifications cannot work without it, and it also
// keeps the shell available so the app can say "Offline" instead of failing to load.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('/sw.js')
  })
}

export default app
