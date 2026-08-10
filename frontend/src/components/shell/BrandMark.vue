<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useAppConfigStore } from '@/stores/appConfigStore'

const { brandName, brandLogoUrl, brandInitial } = storeToRefs(useAppConfigStore())
</script>

<template>
  <img
    v-if="brandLogoUrl"
    class="size-8 flex-none rounded-md object-contain"
    :src="brandLogoUrl"
    :alt="brandName"
  />
  <span
    v-else
    class="grid size-8 flex-none place-items-center rounded-md bg-primary text-[1.05rem] font-bold text-primary-foreground"
  >
    {{ brandInitial }}
  </span>
</template>
