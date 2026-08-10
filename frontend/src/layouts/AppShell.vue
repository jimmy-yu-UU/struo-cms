<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute } from 'vue-router'
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
      <main class="min-w-0 flex-1 overflow-y-auto overflow-x-clip bg-card">
        <div class="px-7 pb-12 pt-6 max-[520px]:px-4 max-[520px]:pb-10 max-[520px]:pt-4">
          <AppBreadcrumb />
          <!-- Key on route.path so params-only navigations between records of the same route
               remount the view (init() re-runs, loads the target item); query changes
               (list page/sort) do not. -->
          <router-view :key="route.path" />
        </div>
      </main>
    </SidebarInset>

    <!-- The app's global outlets, mounted exactly once. -->
    <Toaster position="top-right" />
    <ConfirmHost />
  </SidebarProvider>
</template>
