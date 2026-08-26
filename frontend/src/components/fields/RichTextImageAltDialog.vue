<script setup lang="ts">
import { ref, watch, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

defineOptions({ name: 'RichTextImageAltDialog' })

const props = defineProps<{ open: boolean; alt: string }>()
const emit = defineEmits<{
  (e: 'update:open', open: boolean): void
  (e: 'submit', value: string): void
}>()

const { t } = useI18n()

const alt = ref(props.alt)

// One RichTextInput per field, and an item form can carry several -- this id must not collide
// across two dialogs mounted at once, the same reasoning RichTextLinkDialog already applies.
const altId = useId()

// Reopening the dialog starts from the props again rather than from whatever text was typed (but
// never submitted) during the previous time it was open.
watch(() => props.open, (isOpen) => {
  if (!isOpen) return
  alt.value = props.alt
})

// No validation, unlike RichTextLinkDialog's href field: alt="" is the formal HTML way to mark an
// image as decorative, so an empty value is a legitimate submission, not an empty-input error.
//
// The emit order is load-bearing, not stylistic: RichTextInput releases the image node it captured
// for this dialog on 'update:open'(false), and its submit handler needs that capture. Swap these
// two lines and every alt edit is silently discarded.
function submit(): void {
  emit('submit', alt.value)
  emit('update:open', false)
}

defineExpose({ alt, submit })
</script>

<template>
  <Dialog :open="props.open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-sm">
      <DialogHeader>
        <DialogTitle>{{ t('fields.richtext.altDialogTitle') }}</DialogTitle>
        <!--
          Not decoration. reka points DialogContent's aria-describedby at a DialogDescription id
          whether or not one is rendered, so a dialog without one leaves assistive tech following
          a dangling reference (and warns on mount).
        -->
        <DialogDescription>{{ t('fields.richtext.altDialogDescription') }}</DialogDescription>
      </DialogHeader>
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1.5">
          <Label :for="altId">{{ t('fields.richtext.altLabel') }}</Label>
          <Input :id="altId" v-model="alt" type="text" data-testid="alt" />
        </div>
      </div>
      <DialogFooter>
        <Button type="button" variant="ghost" @click="emit('update:open', false)">{{ t('common.cancel') }}</Button>
        <Button type="button" data-cmd="altSubmit" @click="submit">{{ t('common.confirm') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
