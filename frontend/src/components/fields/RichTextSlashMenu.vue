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
  <div class="z-50 max-h-72 min-w-52 overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
    role="listbox" :aria-label="t('fields.richtext.slashMenu')">
    <!-- This div is the component's only root element and must stay that way: VueRenderer.element
         reads el.firstElementChild once, at construction. Wrapping it in v-if (or otherwise turning
         the compiled root into a comment on the first render) makes that null forever, and
         props.mount() has nothing to mount -- so the empty-list state is a child here, not a
         replacement for the root. This also rules out putting an explanatory comment as a sibling
         of this div at the template root: @vue/compiler-sfc keeps comments in dev/test builds, and a
         comment sitting next to (not inside) the sole root element makes Vue compile the template as
         a multi-root fragment -- the same "root is not a single element" failure, just self-inflicted
         instead of caused by v-if. Comments belong inside this div, as children. -->
    <p v-if="items.length === 0" role="presentation" class="px-2.5 py-1.5 text-sm text-muted-foreground">
      {{ t('fields.richtext.slashNoResults') }}
    </p>
    <!-- preventDefault on mousedown, not a click handler: a mousedown on a non-focusable target's
         default action blurs whatever currently has DOM focus, which here is the editor. Suppressing
         that keeps DOM focus -- and the live ProseMirror selection the command's range is read
         against -- in the editor through the emit, instead of forcing a focus round-trip back into it
         before the range can still be trusted. -->
    <div v-for="(item, i) in items" :id="`${idPrefix}-${item.id}`" :key="item.id" role="option"
      :aria-selected="i === selectedIndex" :data-active="i === selectedIndex"
      class="cursor-pointer rounded px-2.5 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground data-[active=true]:bg-accent data-[active=true]:text-accent-foreground"
      @mousedown.prevent="$emit('select', i)"
      @mouseenter="$emit('hover', i)">
      {{ item.label }}
    </div>
  </div>
</template>
