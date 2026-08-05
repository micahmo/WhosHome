<script lang="ts">
  import { parseRoute } from './lib/router'
  import Admin from './lib/Admin.svelte'
  import Board from './lib/BoardScreen.svelte'
  import Setup from './lib/Setup.svelte'

  // Routes are decided once at load. Nothing here navigates between screens, so there is no
  // reason to listen for history changes.
  const route = parseRoute(window.location.pathname)
</script>

{#if route.name === 'admin'}
  <Admin />
{:else if route.name === 'setup'}
  <Setup token={route.token} />
{:else}
  <Board />
{/if}
