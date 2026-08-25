<script setup lang="ts">
import { ref, computed, watch, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { isAllowedLinkUrl } from '@/lib/linkUrl'

defineOptions({ name: 'RichTextLinkDialog' })

const props = defineProps<{ open: boolean; href: string; newTab: boolean; canRemove: boolean }>()
const emit = defineEmits<{
  (e: 'update:open', open: boolean): void
  (e: 'submit', value: { href: string; newTab: boolean }): void
  (e: 'remove'): void
}>()

const { t } = useI18n()

const href = ref(props.href)
const newTab = ref(props.newTab)

// One RichTextInput per field, and an item form can carry several -- these ids must not collide
// across two dialogs mounted at once, the same reasoning RichTextTableSizeDialog already applies.
const hrefId = useId()
const newTabId = useId()
const errorId = useId()

// Every prefix of a valid URL is itself invalid ("h", "ht", "http", "http:/"...), so a live
// computed would paint the field red on the very first keystroke of normal typing. The error
// text is deferred until the field has been blurred at least once; Confirm staying disabled is
// the live signal in the meantime, the error text is the explanation once the user is done.
const touched = ref(false)
const rejected = computed(() => touched.value && href.value.trim() !== '' && !isAllowedLinkUrl(href.value))
const canSubmit = computed(() => isAllowedLinkUrl(href.value))

// Reopening the dialog starts from the props again rather than from whatever the last rejected
// attempt (including its touched state) left behind.
watch(() => props.open, (isOpen) => {
  if (!isOpen) return
  href.value = props.href
  newTab.value = props.newTab
  touched.value = false
})

// The guard lives here, not only on the button's `disabled`: a caller (or a test) that invokes
// submit directly must get the same refusal.
function submit(): void {
  if (!canSubmit.value) return
  emit('submit', { href: href.value.trim(), newTab: newTab.value })
  emit('update:open', false)
}

// Same reasoning as submit()'s guard: canRemove gates the button's rendering, but a caller (or a
// test) invoking remove() directly through defineExpose must get the same refusal, not a remove
// the host never offered.
function remove(): void {
  if (!props.canRemove) return
  emit('remove')
  emit('update:open', false)
}

defineExpose({ href, newTab, canSubmit, touched, submit, remove })
</script>

<template>
  <Dialog :open="props.open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-sm">
      <DialogHeader>
        <DialogTitle>{{ t('fields.richtext.link') }}</DialogTitle>
      </DialogHeader>
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1.5">
          <Label :for="hrefId">{{ t('fields.richtext.linkPrompt') }}</Label>
          <Input :id="hrefId" v-model="href" type="text" :aria-invalid="rejected"
            :aria-describedby="rejected ? errorId : undefined" data-testid="href" @blur="touched = true" />
        </div>
        <div class="flex items-center gap-2">
          <Checkbox :id="newTabId" v-model="newTab" />
          <Label :for="newTabId">{{ t('fields.richtext.linkOpenInNewTab') }}</Label>
        </div>
        <p v-if="rejected" :id="errorId" class="text-sm text-destructive" role="alert">
          {{ t('fields.richtext.linkUrlInvalid') }}
        </p>
      </div>
      <DialogFooter>
        <Button v-if="props.canRemove" type="button" variant="destructive" data-cmd="linkRemove" @click="remove">
          {{ t('fields.richtext.removeLink') }}
        </Button>
        <Button type="button" variant="ghost" @click="emit('update:open', false)">{{ t('common.cancel') }}</Button>
        <Button type="button" data-cmd="linkSubmit" :disabled="!canSubmit" @click="submit">
          {{ t('common.confirm') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
