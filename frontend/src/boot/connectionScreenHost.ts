import { h, ref, render, type AppContext } from 'vue'
import ConnectionErrorScreen from '@/components/shell/ConnectionErrorScreen.vue'

export type ConnectionScreenHost = {
  // Mounts the screen on first use and resolves on the next Retry click.
  waitForRetry: () => Promise<void>
  setBusy: (busy: boolean) => void
  close: () => void
}

// One connection-error screen stays mounted for the whole boot, however many retries it takes.
// It renders inside the main app's context rather than in a second app: unmounting a second app
// that had installed vue-i18n would dispose the i18n instance every later app shares.
export function createConnectionScreenHost(context: AppContext, container: HTMLElement): ConnectionScreenHost {
  const busy = ref(false)
  let pending: (() => void) | null = null
  let mounted = false

  const Host = {
    render: () => h(ConnectionErrorScreen, { busy: busy.value, onRetry: () => pending?.() }),
  }

  return {
    waitForRetry() {
      if (!mounted) {
        const vnode = h(Host)
        vnode.appContext = context
        render(vnode, container)
        mounted = true
      }
      return new Promise<void>((resolve) => { pending = resolve })
    },
    setBusy(value) { busy.value = value },
    close() {
      if (!mounted) return
      render(null, container)
      mounted = false
    },
  }
}
