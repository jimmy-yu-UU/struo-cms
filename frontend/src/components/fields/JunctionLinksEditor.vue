<script setup lang="ts">
import { ref, computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { X } from '@lucide/vue'
import SortableList from '@/components/form/SortableList.vue'
import RelationPicker from './RelationPicker.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { useAuthStore } from '../../stores/authStore'
import { getFieldType } from '../../lib/fieldTypes/registry'
import {
  visiblePayloadFields, canReadJunction, canWriteJunction, emptyLink, isRelationLinks,
} from '../../lib/junctionLinks'
import type { RelationMeta, FieldMeta } from '../../types/schema'
import type { RelationLink } from '../../types/itemForm'

defineOptions({ name: 'JunctionLinksEditor' })

// The per-row editor for a many-to-many relation whose junction carries payload and/or a SortField
// (spec U2b). RelationInput dispatches here instead of RelationPicker's chip branch when
// usesLinksEditor() says so. Each link is one row: target label, that link's payload inputs (the
// same field-type registry ItemForm uses), a remove button, and — only with a SortField — the
// SortableList arrows. The combobox below the rows is RelationPicker itself with its chips turned
// off; its id[] model is treated purely as the membership set, order always comes from `links`.
const props = defineProps<{ relation: RelationMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: RelationLink[]): void }>()
const { t } = useI18n()
const schema = useSchemaStore()
const auth = useAuthStore()

const links = computed<RelationLink[]>(() => (isRelationLinks(props.modelValue) ? props.modelValue : []))
const ids = computed(() => links.value.map((l) => l.id))
const resolve = (name: string) => schema.get(name)
const access = computed(() => ({
  canRead: (c: string) => auth.canRead(c),
  canWrite: (c: string) => auth.canWrite(c),
  isSuperAdmin: auth.user?.isSuperAdmin === true,
}))
// Permission ladder (spec §3.3): no junction read grant -> the server omits `_junction`, so showing
// inputs would present registry defaults as saved values; hide them. Read but no write -> inputs
// render read-only (buildItemPayload then sends bare ids). The parent's own `disabled` wins over both.
const readable = computed(() => canReadJunction(props.relation, access.value))
const payloadFields = computed<FieldMeta[]>(() => (readable.value ? visiblePayloadFields(props.relation, resolve) : []))
const payloadEditable = computed(() => props.disabled !== true && canWriteJunction(props.relation, resolve, access.value))
const reorderable = computed(() => !!props.relation.sortField)

// Labels come from the picker's own option/label cache (it already resolves every selected id's
// display label for its list) rather than a second round of fetches here.
const picker = ref<{ displayOptions: { id: string; label: string }[] } | null>(null)
function labelFor(id: string): string {
  return picker.value?.displayOptions.find((o) => o.id === id)?.label ?? id
}

function onPickerChange(v: unknown): void {
  const next = ((v as unknown[] | null) ?? []).map(String)
  const nextSet = new Set(next)
  const kept = links.value.filter((l) => nextSet.has(l.id))
  const have = new Set(kept.map((l) => l.id))
  const added = next.filter((id) => !have.has(id)).map((id) => emptyLink(id, payloadFields.value))
  emit('update:modelValue', [...kept, ...added])
}
function removeLink(id: string): void {
  emit('update:modelValue', links.value.filter((l) => l.id !== id))
}
function setPayload(id: string, name: string, v: unknown): void {
  emit('update:modelValue', links.value.map((l) => (l.id === id ? { ...l, junction: { ...l.junction, [name]: v } } : l)))
}
function onReorder(v: RelationLink[]): void {
  emit('update:modelValue', v)
}

// Row- and instance-scoped ids for <label for>, same reasoning as RepeaterField.
const uid = useId()
function fieldId(id: string, name: string): string { return `${uid}-${id}-${name}` }
</script>

<template>
  <div class="junction-links flex w-full max-w-[720px] flex-col gap-3">
    <SortableList
      v-if="links.length"
      :model-value="links"
      :item-key="(l: RelationLink) => l.id"
      :disabled="disabled"
      :reorderable="reorderable"
      @update:model-value="onReorder"
    >
      <template #item="{ item }">
        <div class="junction-link flex w-full flex-col gap-2 rounded-md border p-3" :data-link-id="item.id">
          <div class="flex items-center gap-2">
            <Badge variant="secondary" class="junction-link__label">{{ labelFor(item.id) }}</Badge>
            <span class="flex-1" />
            <!-- type="button": this editor lives inside ItemForm.vue's <form @submit.prevent>. -->
            <Button
              type="button"
              variant="ghost"
              size="icon"
              class="junction-link__remove"
              :aria-label="t('fields.removeOption', { label: labelFor(item.id) })"
              :disabled="disabled"
              @click="removeLink(item.id)"
            >
              <X class="size-4" />
            </Button>
          </div>
          <div v-if="payloadFields.length" class="grid gap-2 sm:grid-cols-2">
            <div v-for="f in payloadFields" :key="f.name" class="junction-link__field flex flex-col gap-1">
              <label class="text-xs text-muted-foreground" :for="fieldId(item.id, f.name)">
                {{ f.label }}<span v-if="f.required" class="text-destructive ml-0.5">*</span>
              </label>
              <component
                :is="getFieldType(f.interface).component"
                :id="fieldId(item.id, f.name)"
                :field="f"
                :model-value="item.junction[f.name]"
                :disabled="!payloadEditable || f.readOnly"
                @update:model-value="(v: unknown) => setPayload(item.id, f.name, v)"
              />
            </div>
          </div>
        </div>
      </template>
    </SortableList>
    <p v-else class="junction-links__empty text-sm italic text-muted-foreground">{{ t('fields.noItems') }}</p>

    <RelationPicker
      ref="picker"
      :relation="relation"
      :model-value="ids"
      multiple
      :show-chips="false"
      :disabled="disabled"
      @update:model-value="onPickerChange"
    />
  </div>
</template>
