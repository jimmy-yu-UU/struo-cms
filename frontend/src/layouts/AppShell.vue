<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute } from 'vue-router'
import Toast from 'primevue/toast'
import { SidebarInset, SidebarProvider } from '@/components/ui/sidebar'
import { Toaster } from '@/components/ui/sonner'
import { useSchemaStore } from '@/stores/schemaStore'
import TheTopbar from '@/components/shell/TheTopbar.vue'
import TheSidebar from '@/components/shell/TheSidebar.vue'
import AppBreadcrumb from '@/components/shell/AppBreadcrumb.vue'
import ConfirmHost from '@/components/shell/ConfirmHost.vue'

const schema = useSchemaStore()
const route = useRoute()

onMounted(() => { schema.load() })
</script>

<template>
  <SidebarProvider>
    <TheSidebar />
    <SidebarInset>
      <TheTopbar />
      <!-- SidebarInset already renders the document's <main data-slot="sidebar-inset"> — a
           second nested <main> here would be invalid HTML and give screen readers two
           competing "main content" landmarks, so this is a plain <div>.
           No overflow-y-auto: SidebarInset (vendored, read-only) never gives this div a
           bounded height to scroll within (min-h-svh is a floor, not a cap), so that class
           was dead — scrolling genuinely happens at the document level; see TheTopbar's
           sticky header for how the topbar stays pinned through that. overflow-x-clip is
           real and stays: it bounds horizontal overflow regardless of vertical scroll model. -->
      <div class="min-w-0 flex-1 overflow-x-clip bg-card">
        <div class="px-7 pb-12 pt-6 max-[520px]:px-4 max-[520px]:pb-10 max-[520px]:pt-4">
          <AppBreadcrumb />
          <!-- Key on route.path so params-only navigations between records of the same route
               remount the view (init() re-runs, loads the target item); query changes
               (list page/sort) do not. -->
          <router-view :key="route.path" />
        </div>
      </div>
    </SidebarInset>

    <!-- The app's global outlets, mounted exactly once. Toast (PrimeVue) stays alongside
         Toaster (shadcn/vue-sonner) even though no production code calls `primevue/usetoast`
         anymore: `main.ts` still registers ToastService, and ToastService.add() emits on
         ToastEventBus with no host subscribed (silently, no error) if this host is removed
         before that registration goes too. -->
    <Toast position="top-right" />
    <Toaster position="top-right" />
    <ConfirmHost />
  </SidebarProvider>
</template>
