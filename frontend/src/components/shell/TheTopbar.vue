<script setup lang="ts">
import { SidebarTrigger } from '@/components/ui/sidebar'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import ThemeToggle from './ThemeToggle.vue'
import UserMenu from './UserMenu.vue'
</script>

<template>
  <!-- brand-spec §3: 56px bar, SidebarTrigger leftmost, no brand — the brand button lives in
       SidebarHeader now, and the breadcrumb sits in the content area, not here. -->
  <header class="flex h-14 shrink-0 items-center gap-2 border-b border-border bg-card px-4">
    <SidebarTrigger />
    <div class="flex-1" />
    <UiLanguageSwitcher />
    <ThemeToggle />
    <UserMenu />
  </header>
</template>
