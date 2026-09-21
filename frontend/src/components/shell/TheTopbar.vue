<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { SidebarTrigger } from '@/components/ui/sidebar'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import ThemeToggle from './ThemeToggle.vue'
import UserMenu from './UserMenu.vue'

const { t } = useI18n()
</script>

<template>
  <!-- 56px bar, SidebarTrigger leftmost, no brand — the brand button lives in
       SidebarHeader now, and the breadcrumb sits in the content area, not here.
       sticky top-0: SidebarInset (vendored, read-only) is min-h-svh not h-svh — a floor, not a
       cap — so on content taller than the viewport the column grows and the document scrolls
       past a plain shrink-0 header instead of the header staying pinned. sticky keeps it glued
       to the top of whichever ancestor actually scrolls (the document here, since nothing
       between this header and the viewport sets overflow). -->
  <header class="sticky top-0 z-10 flex h-14 shrink-0 items-center gap-2 border-b border-border bg-card px-4">
    <!-- SidebarTrigger doesn't set inheritAttrs: false and its root is <Button>, so this
         sets a localized accessible name — the vendored component only ships a hardcoded
         English "Toggle Sidebar" sr-only span. -->
    <SidebarTrigger :aria-label="t('shell.openMenu')" />
    <div class="flex-1" />
    <UiLanguageSwitcher />
    <ThemeToggle />
    <UserMenu />
  </header>
</template>
