<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/authStore'
import { useSchemaStore } from '../../stores/schemaStore'
import { useSidebarStore } from '../../stores/sidebarStore'
import { buildNav } from '../../lib/buildNav'
import SidebarNavItem from './SidebarNavItem.vue'

const UNGROUPED = 'General'

const auth = useAuthStore()
const schema = useSchemaStore()
const sidebar = useSidebarStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const canReadMedia = computed(
  () => auth.user?.isSuperAdmin === true || auth.user?.permissions?.file?.read === true,
)

const isSuperAdmin = computed(() => auth.user?.isSuperAdmin === true)

const groups = computed(() =>
  buildNav(schema.collections, auth.user?.isSuperAdmin ?? false, auth.user?.permissions ?? {}),
)

// Expanded state per real group (default open). Keyed by group name.
const open = reactive<Record<string, boolean>>({})
function isOpen(group: string): boolean {
  return open[group] ?? true
}
function toggleGroup(group: string): void {
  // Collapsed rail: a group click means "let me navigate" — expand the sidebar and
  // make sure the group is open (prototype behavior), never toggle it shut blindly.
  if (sidebar.collapsed) {
    sidebar.expand()
    open[group] = true
    return
  }
  open[group] = !isOpen(group)
}

function activeCollection(): string | null {
  const r = route.name as string | undefined
  if (r === 'collection-list' || r === 'collection-create' || r === 'collection-item') {
    return (route.params as Record<string, string>).name ?? null
  }
  return null
}

function go(to: { name: string; params?: Record<string, string> }): void {
  router.push(to)
  sidebar.closeDrawer()
}
</script>

<template>
  <aside class="sidebar" :class="{ collapsed: sidebar.collapsed }" :aria-label="t('shell.mainNav')">
    <div v-if="schema.loadError" class="nav-error" role="alert">
      <span>{{ schema.loadError }}</span>
      <button type="button" @click="schema.load()">{{ t('common.retry') }}</button>
    </div>
    <template v-else>
      <!-- System (pinned) -->
      <SidebarNavItem
        :label="t('nav.dashboard')"
        icon="pi pi-th-large"
        :active="route.name === 'dashboard'"
        @activate="go({ name: 'dashboard' })"
      />
      <SidebarNavItem
        v-if="canReadMedia"
        :label="t('nav.media')"
        icon="pi pi-images"
        :active="route.name === 'media'"
        @activate="go({ name: 'media' })"
      />
      <SidebarNavItem
        v-if="isSuperAdmin"
        :label="t('nav.settings')"
        icon="pi pi-cog"
        :active="route.name === 'settings'"
        @activate="go({ name: 'settings' })"
      />
      <hr class="nav-sep" aria-hidden="true" />

      <!-- Collections -->
      <template v-for="g in groups" :key="g.group">
        <template v-if="g.group === UNGROUPED">
          <SidebarNavItem
            v-for="it in g.items"
            :key="it.name"
            :label="it.label"
            :icon="it.icon"
            :active="activeCollection() === it.name"
            @activate="go({ name: 'collection-list', params: { name: it.name } })"
          />
        </template>
        <div v-else class="nav-group" :class="{ open: isOpen(g.group) }">
          <button
            type="button"
            class="nav-item nav-parent"
            :aria-expanded="isOpen(g.group)"
            @click="toggleGroup(g.group)"
          >
            <i class="nav-icon pi pi-folder" aria-hidden="true" />
            <span class="nav-label">{{ g.group }}</span>
            <i class="pi pi-angle-down nav-chev" aria-hidden="true" />
          </button>
          <div v-show="isOpen(g.group)" class="nav-sub">
            <SidebarNavItem
              v-for="it in g.items"
              :key="it.name"
              :label="it.label"
              :icon="it.icon"
              :active="activeCollection() === it.name"
              @activate="go({ name: 'collection-list', params: { name: it.name } })"
            />
          </div>
        </div>
      </template>

      <div class="side-foot">
        <button
          type="button"
          class="nav-item collapse-btn only-desktop"
          :aria-label="sidebar.collapsed ? t('shell.expand') : t('shell.collapse')"
          @click="sidebar.toggleCollapse()"
        >
          <i class="pi pi-angle-left collapse-chev" aria-hidden="true" />
          <span class="nav-label">{{ t('shell.collapse') }}</span>
        </button>
        <p class="caption side-ver">v0.9.0 · {{ t('shell.version') }}</p>
      </div>
    </template>
  </aside>
</template>
