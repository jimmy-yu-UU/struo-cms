<!-- frontend/src/components/CollectionNav.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import PanelMenu from 'primevue/panelmenu'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { buildNav } from '../lib/buildNav'

const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()

const canReadMedia = computed(
  () => auth.user?.isSuperAdmin === true || auth.user?.permissions?.file?.read === true,
)

const model = computed(() => [
  ...(canReadMedia.value
    ? [
        {
          key: 'media',
          label: 'Media',
          items: [{ key: 'media', label: 'Media Library', command: () => router.push({ name: 'media' }) }],
        },
      ]
    : []),
  ...buildNav(schema.collections, auth.user?.isSuperAdmin ?? false, auth.user?.permissions ?? {}).map(
    (g) => ({
      key: g.group,
      label: g.group,
      items: g.items.map((it) => ({
        key: it.name,
        label: it.label,
        command: () => router.push({ name: 'collection-list', params: { name: it.name } }),
      })),
    }),
  ),
])

defineExpose({ model })
</script>

<template>
  <div v-if="schema.loadError" class="nav-error" role="alert">
    <span>{{ schema.loadError }}</span>
    <button type="button" @click="schema.load()">Retry</button>
  </div>
  <PanelMenu v-else :model="model" />
</template>
