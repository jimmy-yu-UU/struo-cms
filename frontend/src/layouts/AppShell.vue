<script setup lang="ts">
import { onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Toast from 'primevue/toast'
import { SidebarProvider } from '@/components/ui/sidebar'
import { useSchemaStore } from '../stores/schemaStore'
import { useSidebarStore } from '../stores/sidebarStore'
import TheTopbar from '../components/shell/TheTopbar.vue'
import TheSidebar from '../components/shell/TheSidebar.vue'
import AppBreadcrumb from '../components/shell/AppBreadcrumb.vue'

const schema = useSchemaStore()
const sidebar = useSidebarStore()
const route = useRoute()
const { t } = useI18n()

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && sidebar.drawerOpen) sidebar.closeDrawer()
}

onMounted(() => {
  schema.load()
  window.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown)
})

watch(
  () => route.path,
  () => sidebar.closeDrawer(),
)
</script>

<template>
  <!-- Minimal fix to keep the app runnable: SidebarTrigger (TheTopbar) and useSidebar
       (TheSidebar) both inject a SidebarProvider context and throw without one. Everything
       else here — useSidebarStore, the .shell/.scrim markup, Toast — is Task 10's to replace
       with SidebarInset/Toaster/ConfirmHost; not pulled forward here. -->
  <SidebarProvider>
    <div class="shell" :class="{ collapsed: sidebar.collapsed, drawer: sidebar.drawerOpen }">
      <TheTopbar />
      <TheSidebar />
      <button
        v-if="sidebar.drawerOpen"
        type="button"
        class="scrim"
        :aria-label="t('shell.closeMenu')"
        @click="sidebar.closeDrawer()"
      />
      <main class="content">
        <div class="page">
          <AppBreadcrumb />
          <!-- Key on route.path so params-only navigations between records of the same route remount the
               view (init() re-runs, loads the target item); query changes (list page/sort) do not. -->
          <router-view :key="route.path" />
        </div>
      </main>
      <Toast position="top-right" />
    </div>
  </SidebarProvider>
</template>
