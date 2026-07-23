<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import Menu from 'primevue/menu'
import { useAuthStore } from '../../stores/authStore'

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()

const roleLabel = computed(() => (auth.user?.isSuperAdmin ? t('user.superAdmin') : t('user.member')))
const displayName = computed(() => auth.user?.name || auth.user?.email || roleLabel.value)

async function onLogout() {
  await auth.logout()
  router.push({ name: 'login' })
}

const menuModel = computed(() => [
  { label: t('common.logout'), icon: 'pi pi-sign-out', command: onLogout },
])

const menu = ref<InstanceType<typeof Menu> | null>(null)
function toggle(event: Event) {
  menu.value?.toggle(event)
}

defineExpose({ menuModel })
</script>

<template>
  <div class="user-wrap">
    <button
      type="button"
      class="user-btn"
      :aria-label="t('user.account')"
      aria-haspopup="true"
      @click="toggle"
    >
      <span class="avatar"><i class="pi pi-user" aria-hidden="true" /></span>
      <span class="user-name">
        <b>{{ displayName }}</b>
        <span class="user-role">{{ roleLabel }}</span>
      </span>
    </button>
    <Menu ref="menu" :model="menuModel" :popup="true">
      <template #start>
        <div class="user-menu-head">
          <b>{{ displayName }}</b>
          <span v-if="auth.user?.email && auth.user?.name" class="user-menu-email">{{ auth.user.email }}</span>
        </div>
      </template>
    </Menu>
  </div>
</template>

<style scoped>
.user-menu-head { display: grid; gap: 2px; padding: 10px 12px 8px; border-bottom: 1px solid var(--border); margin-bottom: 4px; }
.user-menu-head b { font-size: .9rem; }
.user-menu-email { font-size: .78rem; color: var(--muted); }
</style>
