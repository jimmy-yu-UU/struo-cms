<script setup lang="ts">
defineProps<{ caption: string; value: number | string }>()
</script>

<template>
  <div class="card stat">
    <span class="stat__caption">{{ caption }}</span>
    <b class="stat__value">{{ value }}</b>
  </div>
</template>

<style scoped>
.stat {
  padding: 18px 20px;
  display: grid;
  gap: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--surface);
}
.stat__caption {
  color: var(--muted);
  font-size: 0.85rem;
}
.stat__value {
  font-family: var(--mono);
  font-size: 1.85rem;
  font-weight: 650;
  letter-spacing: -0.02em;
  color: var(--fg);
}
</style>
