<script setup lang="ts">
import { ref, computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ArrowDown, ArrowUp, X } from '@lucide/vue'
import { reorder } from '@/components/form/sortableReorder'
import RelationPicker from './RelationPicker.vue'
import FieldInput from './FieldInput.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { useAuthStore } from '../../stores/authStore'
import {
  visiblePayloadFields, canReadJunction, canWriteJunction, emptyLink, isRelationLinks, junctionAccessFrom,
} from '../../lib/junctionLinks'
import type { RelationMeta, FieldMeta } from '../../types/schema'
import type { RelationLink } from '../../types/itemForm'

defineOptions({ name: 'JunctionLinksEditor' })

// The per-row editor for a many-to-many relation whose junction carries payload and/or a SortField
// (spec U2b). RelationInput dispatches here instead of RelationPicker's chip branch when
// usesLinksEditor() says so. One field: a muted description above a single bordered box that holds a
// header row, one row per link (order buttons, only with a SortField; the target label; that link's
// payload inputs, the same field-type registry ItemForm uses; a remove button) and, as its last row, an
// inline "Add {target}…" control, which is RelationPicker itself with its chips turned off and its box
// drawn as a row. Its id[] model is treated purely as the membership set, order always comes from
// `links`. Rows and header share one grid template on a wide screen; on a narrow one a row stacks:
// target, payload inputs, then the buttons in a compact row.
const props = defineProps<{ relation: RelationMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: RelationLink[]): void }>()
const { t } = useI18n()
const schema = useSchemaStore()
const auth = useAuthStore()

const links = computed<RelationLink[]>(() => (isRelationLinks(props.modelValue) ? props.modelValue : []))
const ids = computed(() => links.value.map((l) => l.id))
const resolve = (name: string) => schema.get(name)
const access = computed(() => junctionAccessFrom(auth))
// Permission ladder (spec §3.3): no junction read grant -> the server omits `_junction`, so showing
// inputs would present registry defaults as saved values; hide them. Read but no write -> inputs
// render read-only (buildItemPayload then sends bare ids). The parent's own `disabled` wins over both.
const readable = computed(() => canReadJunction(props.relation, access.value))
const payloadFields = computed<FieldMeta[]>(() => (readable.value ? visiblePayloadFields(props.relation, resolve) : []))
const payloadEditable = computed(() => props.disabled !== true && canWriteJunction(props.relation, resolve, access.value))
const reorderable = computed(() => !!props.relation.sortField)
const targetLabel = computed(() => schema.get(props.relation.targetCollection)?.label ?? props.relation.label)
// Fixed widths for the two button columns, so the header and every row (each its own grid) line up.
const gridTemplate = computed(() => [
  reorderable.value ? '4.5rem' : null,
  'minmax(0, 1.2fr)',
  ...payloadFields.value.map(() => 'minmax(0, 1.6fr)'),
  '2.5rem',
].filter((c) => c !== null).join(' '))

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
function move(from: number, to: number): void {
  // reorder() hands back the same array for an out-of-range move: nothing changed, nothing to emit.
  const next = reorder(links.value, from, to)
  if (next !== links.value) emit('update:modelValue', next)
}

// Row- and instance-scoped ids for <label for>, same reasoning as RepeaterField.
const uid = useId()
function fieldId(id: string, name: string): string { return `${uid}-${id}-${name}` }
</script>

<template>
  <div class="junction-links flex w-full max-w-[880px] flex-col gap-1.5">
    <p class="junction-links__description text-sm text-muted-foreground">{{ t('fields.junctionDescription') }}</p>

    <div data-testid="junction-box" class="overflow-hidden rounded-md border border-input bg-transparent">
      <div class="divide-y">
        <div
          v-if="links.length"
          data-testid="junction-header"
          class="hidden gap-3 px-3 py-2 text-xs font-medium text-muted-foreground sm:grid"
          :style="{ gridTemplateColumns: gridTemplate }"
        >
          <span v-if="reorderable">{{ t('fields.junctionOrder') }}</span>
          <span>{{ targetLabel }}</span>
          <span v-for="f in payloadFields" :key="f.name">{{ f.label }}<span v-if="f.required" class="text-destructive ml-0.5">*</span></span>
          <span class="sr-only">{{ t('fields.junctionRemoveHeader') }}</span>
        </div>

        <ul v-if="links.length" class="divide-y">
          <li
            v-for="(item, i) in links"
            :key="item.id"
            class="junction-link flex w-full flex-col gap-2 p-3 sm:grid sm:items-center sm:gap-3"
            :style="{ gridTemplateColumns: gridTemplate }"
            :data-link-id="item.id"
          >
            <Badge variant="secondary" class="junction-link__label order-1 w-fit max-w-full justify-self-start sm:order-2">
              {{ labelFor(item.id) }}
            </Badge>
            <div
              v-for="f in payloadFields"
              :key="f.name"
              class="junction-link__field order-2 flex min-w-0 flex-col gap-1 sm:order-3"
            >
              <!-- Visible labels while the row is stacked; the header names the columns on a wide screen. -->
              <label class="text-xs text-muted-foreground sm:sr-only" :for="fieldId(item.id, f.name)">
                {{ f.label }}<span v-if="f.required" class="text-destructive ml-0.5">*</span>
              </label>
              <FieldInput
                :id="fieldId(item.id, f.name)"
                :field="f"
                :model-value="item.junction[f.name]"
                :disabled="!payloadEditable"
                @update:model-value="(v: unknown) => setPayload(item.id, f.name, v)"
              />
            </div>
            <!-- A compact row of buttons while stacked; on a wide screen the wrapper disappears and each
                 button is a cell of the row grid. type="button": this editor lives inside ItemForm.vue's
                 <form @submit.prevent>. A disabled button gets no pointer events, so it keeps the default
                 cursor; an enabled one shows the pointer. -->
            <div class="order-3 flex items-center gap-1 sm:contents">
              <div v-if="reorderable" class="flex items-center gap-1 sm:order-1">
                <Button
                  type="button"
                  data-testid="move-up"
                  variant="outline"
                  size="icon-sm"
                  class="cursor-pointer disabled:opacity-40"
                  :title="t('fields.moveUp')"
                  :aria-label="t('fields.moveUpLabel', { label: labelFor(item.id) })"
                  :disabled="disabled || i === 0"
                  @click="move(i, i - 1)"
                >
                  <ArrowUp class="size-4" />
                </Button>
                <Button
                  type="button"
                  data-testid="move-down"
                  variant="outline"
                  size="icon-sm"
                  class="cursor-pointer disabled:opacity-40"
                  :title="t('fields.moveDown')"
                  :aria-label="t('fields.moveDownLabel', { label: labelFor(item.id) })"
                  :disabled="disabled || i === links.length - 1"
                  @click="move(i, i + 1)"
                >
                  <ArrowDown class="size-4" />
                </Button>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                class="junction-link__remove ml-auto cursor-pointer justify-self-end disabled:opacity-40 sm:order-4 sm:ml-0"
                :aria-label="t('fields.removeOption', { label: labelFor(item.id) })"
                :disabled="disabled"
                @click="removeLink(item.id)"
              >
                <X class="size-4" />
              </Button>
            </div>
          </li>
        </ul>
        <p v-else-if="disabled" class="junction-links__empty px-3 py-2 text-sm italic text-muted-foreground">{{ t('fields.noItems') }}</p>

        <!-- The last row of the box: adding is part of the field, not a section of its own. -->
        <div v-if="!disabled" data-testid="junction-add">
          <RelationPicker
            ref="picker"
            :relation="relation"
            :model-value="ids"
            multiple
            :show-chips="false"
            :add-label="t('fields.junctionAdd', { target: targetLabel })"
            @update:model-value="onPickerChange"
          />
        </div>
      </div>
    </div>
    <!-- A read-only field offers no add row; this picker stays mounted, hidden, only to resolve the labels. -->
    <RelationPicker
      v-if="disabled"
      ref="picker"
      class="hidden"
      aria-hidden="true"
      :relation="relation"
      :model-value="ids"
      multiple
      :show-chips="false"
      disabled
    />
  </div>
</template>
