<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useAppConfigStore } from '../../stores/appConfigStore'

const appConfig = useAppConfigStore()
const { brandName, brandLogoUrl, brandInitial } = storeToRefs(appConfig)
</script>

<template>
  <img
    v-if="brandLogoUrl"
    class="brand-logo"
    :src="brandLogoUrl"
    :alt="brandName"
  />
  <span v-else class="mark">{{ brandInitial }}</span>
</template>

<style scoped>
.brand-logo {
  width: 32px;
  height: 32px;
  flex: none;
  border-radius: 8px;
  object-fit: contain;
}
.mark {
  width: 32px;
  height: 32px;
  flex: none;
  border-radius: 8px;
  background: var(--accent);
  color: #fff;
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 1.05rem;
}
</style>
