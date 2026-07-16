<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import Toast from 'primevue/toast'
import { useSchemaStore } from '../stores/schemaStore'
import { useSidebarStore } from '../stores/sidebarStore'
import TheTopbar from '../components/shell/TheTopbar.vue'
import TheSidebar from '../components/shell/TheSidebar.vue'
import AppBreadcrumb from '../components/shell/AppBreadcrumb.vue'

const schema = useSchemaStore()
const sidebar = useSidebarStore()
const route = useRoute()

onMounted(() => {
  schema.load()
})

// Close the mobile drawer whenever the route changes. Re-invoke useRoute() inside the
// getter (rather than reading the captured `route` binding) so the watcher re-tracks the
// router's current route object on every check — needed because vue-router's real route
// object mutates in place, but test doubles (and some route-object replacements) swap in a
// whole new object instead of mutating the existing one.
watch(
  () => useRoute().path,
  () => sidebar.closeDrawer(),
)
</script>

<template>
  <div class="shell" :class="{ collapsed: sidebar.collapsed, drawer: sidebar.drawerOpen }">
    <TheTopbar />
    <TheSidebar />
    <button
      v-if="sidebar.drawerOpen"
      type="button"
      class="scrim"
      aria-label="關閉導覽選單"
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
</template>
