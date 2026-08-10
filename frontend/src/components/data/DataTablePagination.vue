<script setup lang="ts">
import { computed } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'

const props = defineProps<{ page: number; pageSize: number; total: number }>()
const emit = defineEmits<{ 'update:page': [number] }>()

const pageCount = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
// Humans count from 1; the backend offsets from 0. Reuse collectionList.range so the wording
// stays identical to the TableFooter this replaces.
const from = computed(() => (props.total === 0 ? 0 : props.page * props.pageSize + 1))
const to = computed(() => Math.min((props.page + 1) * props.pageSize, props.total))

const isFirst = computed(() => props.page <= 0)
const isLast = computed(() => props.page >= pageCount.value - 1)
</script>

<template>
  <div class="flex items-center justify-between gap-4 pt-3">
    <span class="text-sm text-muted-foreground">
      {{ $t('collectionList.range', { from, to, total: props.total }) }}
    </span>
    <div class="flex items-center gap-1">
      <Button
        variant="outline" size="icon" data-testid="pagination-prev"
        :disabled="isFirst" :aria-label="$t('common.previous')"
        @click="emit('update:page', props.page - 1)"
      >
        <ChevronLeft class="size-4" aria-hidden="true" />
      </Button>
      <Button
        variant="outline" size="icon" data-testid="pagination-next"
        :disabled="isLast" :aria-label="$t('common.next')"
        @click="emit('update:page', props.page + 1)"
      >
        <ChevronRight class="size-4" aria-hidden="true" />
      </Button>
    </div>
  </div>
</template>
