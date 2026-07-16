<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import CollectionNav from '../components/CollectionNav.vue'

const auth = useAuthStore()
const schema = useSchemaStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

onMounted(() => {
  schema.load()
})

async function onLogout() {
  await auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <header>
      <span class="brand">StruoCMS</span>
      <button type="button" class="logout" @click="onLogout">{{ t('common.logout') }}</button>
    </header>
    <nav><CollectionNav /></nav>
    <!-- Key on route.path so params-only navigations between records of the same route (e.g. a
         RelatedList row click collection-item/A -> collection-item/B, or create -> edit) remount the
         view: init() re-runs and the form loads the target item instead of reusing stale data.
         `path` excludes query, so list page/sort (component-local state) never force a remount. -->
    <main><router-view :key="route.path" /></main>
  </div>
</template>
