<!-- frontend/src/components/common/PageHeader.vue -->
<script setup lang="ts">
defineProps<{ title: string; caption?: string }>()
</script>

<template>
  <header class="page-head">
    <div v-if="$slots.lead" class="head-lead">
      <slot name="lead" />
    </div>
    <div class="titles">
      <h1>{{ title }}</h1>
      <p v-if="caption" class="text-sm font-medium text-muted-foreground">{{ caption }}</p>
    </div>
    <div class="head-actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.page-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 20px;
}
.head-lead { display: flex; align-items: center; }
.titles { display: grid; gap: 4px; flex: 1 1 auto; }
.titles h1 { margin: 0; font-size: 1.5rem; color: var(--fg); }
.head-actions { display: flex; gap: 10px; align-items: center; }
</style>
