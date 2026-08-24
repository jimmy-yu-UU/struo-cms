<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { HEADING_LEVELS, type HeadingLevel } from './richTextHeadings'

defineOptions({ name: 'RichTextHeadingMenu' })

const props = defineProps<{ disabled?: boolean; activeLevel: HeadingLevel | null }>()
const emit = defineEmits<{ (e: 'select', level: HeadingLevel | null): void }>()

const { t } = useI18n()
const open = ref(false)
const levels = HEADING_LEVELS

function pick(level: HeadingLevel | null): void {
  emit('select', level)
  open.value = false
}

function label(level: HeadingLevel | null): string {
  return level === null ? t('fields.richtext.paragraph') : t(`fields.richtext.heading${level}`)
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!-- type="button" is explicit even though PopoverTrigger (as-child) merges its own
           type="button" onto what it wraps: this sits inside ItemForm.vue's <form>, so the Button
           does not rely on the merge behaviour of the component wrapping it. -->
      <Button type="button" variant="ghost" size="sm" data-cmd="headings" :disabled="disabled"
        :aria-label="`${t('fields.richtext.headings')}: ${label(props.activeLevel)}`" :title="t('fields.richtext.headings')">
        {{ label(props.activeLevel) }}
      </Button>
    </PopoverTrigger>
    <PopoverContent class="flex w-auto min-w-40 flex-col gap-0.5 p-1">
      <button type="button" data-cmd="paragraph" :data-active="props.activeLevel === null"
        class="rounded px-2.5 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground data-[active=true]:bg-accent"
        @click="pick(null)">{{ t('fields.richtext.paragraph') }}</button>
      <button v-for="lvl in levels" :key="lvl" type="button" :data-cmd="`h${lvl}`"
        :data-active="props.activeLevel === lvl"
        class="rounded px-2.5 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground data-[active=true]:bg-accent"
        @click="pick(lvl)">{{ t(`fields.richtext.heading${lvl}`) }}</button>
    </PopoverContent>
  </Popover>
</template>
