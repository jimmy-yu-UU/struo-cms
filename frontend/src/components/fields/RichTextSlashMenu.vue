<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { RichTextSlashItem } from './richTextSlashCommands'

defineOptions({ name: 'RichTextSlashMenu' })

defineProps<{
  items: ReadonlyArray<RichTextSlashItem>
  selectedIndex: number
  idPrefix: string
}>()
defineEmits<{ (e: 'select', index: number): void; (e: 'hover', index: number): void }>()

const { t } = useI18n()
</script>

<template>
  <div class="z-50 max-h-72 min-w-52 overflow-y-auto rounded-md border bg-popover p-1 shadow-md"
    role="listbox" :aria-label="t('fields.richtext.slashMenu')">
    <!-- This div is the component's only root element and must stay that way: VueRenderer.element
         reads el.firstElementChild once, at construction. Wrapping it in v-if (or otherwise turning
         the compiled root into a comment on the first render) makes that null forever, and
         props.mount() has nothing to mount -- so the empty-list state is a child here, not a
         replacement for the root. -->
    <p v-if="items.length === 0" class="px-2.5 py-1.5 text-sm text-muted-foreground">
      {{ t('fields.richtext.slashNoResults') }}
    </p>
    <!-- mousedown, not click: a click's own mousedown blurs the editor first, and the suggestion
         plugin's state is torn down with that focus loss before a click handler would ever fire. -->
    <div v-for="(item, i) in items" :id="`${idPrefix}-${item.id}`" :key="item.id" role="option"
      :aria-selected="i === selectedIndex" :data-active="i === selectedIndex"
      class="cursor-pointer rounded px-2.5 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground data-[active=true]:bg-accent"
      @mousedown.prevent="$emit('select', i)"
      @mouseenter="$emit('hover', i)">
      {{ item.label }}
    </div>
  </div>
</template>
