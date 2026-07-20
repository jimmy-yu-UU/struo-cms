<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import { useSidebarStore } from '../../stores/sidebarStore'
import { useAppConfigStore } from '../../stores/appConfigStore'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import ThemeToggle from './ThemeToggle.vue'
import UserMenu from './UserMenu.vue'
import BrandMark from './BrandMark.vue'

const sidebar = useSidebarStore()
const router = useRouter()
const { t } = useI18n()
const { brandName } = storeToRefs(useAppConfigStore())
</script>

<template>
  <header class="topbar">
    <button
      type="button"
      class="icon-btn only-mobile drawer-toggle"
      :aria-label="t('shell.openMenu')"
      @click="sidebar.toggleDrawer()"
    >
      <i class="pi pi-bars" aria-hidden="true" />
    </button>
    <button
      type="button"
      class="brand brand-btn"
      :aria-label="t('shell.brandHome')"
      @click="router.push({ name: 'dashboard' })"
    >
      <BrandMark /><b>{{ brandName }}</b>
    </button>
    <div class="spacer" />
    <UiLanguageSwitcher />
    <ThemeToggle />
    <UserMenu />
  </header>
</template>
