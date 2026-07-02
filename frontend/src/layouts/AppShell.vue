<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import CollectionNav from '../components/CollectionNav.vue'

const auth = useAuthStore()
const schema = useSchemaStore()
const router = useRouter()

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
      <button type="button" class="logout" @click="onLogout">Log out</button>
    </header>
    <nav><CollectionNav /></nav>
    <main><router-view /></main>
  </div>
</template>
