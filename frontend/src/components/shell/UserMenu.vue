<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { LogOut, User } from '@lucide/vue'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem,
  DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useAuthStore } from '@/stores/authStore'

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()

const roleLabel = computed(() => (auth.user?.isSuperAdmin ? t('user.superAdmin') : t('user.member')))
const displayName = computed(() => auth.user?.name || auth.user?.email || roleLabel.value)

async function onLogout(): Promise<void> {
  await auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <button
        type="button"
        class="flex items-center gap-2.5 rounded-full py-1 pl-1 pr-2.5 hover:bg-accent"
        :aria-label="t('user.account')"
      >
        <span class="grid size-8 place-items-center rounded-full border border-border bg-background text-muted-foreground">
          <User class="size-4" aria-hidden="true" />
        </span>
        <span class="grid text-left leading-tight max-[1023px]:hidden">
          <b class="text-[0.83rem]">{{ displayName }}</b>
          <span class="text-[0.7rem] text-muted-foreground">{{ roleLabel }}</span>
        </span>
      </button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuLabel class="grid gap-0.5">
        <b class="text-sm">{{ displayName }}</b>
        <span v-if="auth.user?.email && auth.user?.name" class="text-xs font-normal text-muted-foreground">
          {{ auth.user.email }}
        </span>
      </DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuItem @select="onLogout">
        <LogOut class="size-4" aria-hidden="true" />
        {{ t('common.logout') }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
