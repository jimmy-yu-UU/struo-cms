import { defineStore } from 'pinia'

function readInitialCollapsed(): boolean {
  try {
    return localStorage.getItem('struo.sidebar') === 'collapsed'
  } catch {
    return false
  }
}

export const useSidebarStore = defineStore('sidebar', {
  state: () => ({ collapsed: readInitialCollapsed(), drawerOpen: false }),
  actions: {
    toggleCollapse(): void {
      this.collapsed = !this.collapsed
      try {
        localStorage.setItem('struo.sidebar', this.collapsed ? 'collapsed' : 'expanded')
      } catch {
        /* localStorage unavailable — state still applied in-memory */
      }
    },
    expand(): void {
      if (!this.collapsed) return
      this.collapsed = false
      try {
        localStorage.setItem('struo.sidebar', 'expanded')
      } catch {
        /* localStorage unavailable — state still applied in-memory */
      }
    },
    openDrawer(): void {
      this.drawerOpen = true
    },
    closeDrawer(): void {
      this.drawerOpen = false
    },
    toggleDrawer(): void {
      this.drawerOpen = !this.drawerOpen
    },
  },
})
