<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/authStore'

const auth = useAuthStore()
const router = useRouter()

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
    <nav><!-- collection nav placeholder (Phase 7b) --></nav>
    <main><router-view /></main>
  </div>
</template>
