<script setup lang="ts">
import { ref, computed, watch, useId, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Badge } from '@/components/ui/badge'
import FieldInput from './fields/FieldInput.vue'
import RelationInput from './fields/RelationInput.vue'
import { splitFields } from '../lib/splitFields'
import { hasLocaleContent } from '../lib/localeCompleteness'
import { localeHasErrors, translationErrorKey } from '../lib/translationErrors'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const props = defineProps<{
  meta: CollectionMeta
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  itemId?: string
  // Returns a reason when an option of the named relation cannot be chosen.
  relationOptionHint?: (relation: string, raw: Record<string, unknown>) => string | null
}>()
// The form edits the parent's draft object in place; declaring it as a model makes that
// two-way contract explicit to consumers.
const model = defineModel<FormModel>('model', { required: true })
const emit = defineEmits<{ (e: 'submit'): void }>()
const { t } = useI18n()

const fields = computed(() => splitFields(props.meta))
function optionHintFor(relation: string): ((raw: Record<string, unknown>) => string | null) | undefined {
  const hint = props.relationOptionHint
  return hint ? (raw) => hint(relation, raw) : undefined
}
const defaultCode = computed(() => props.locales.find((l) => l.isDefault)?.code ?? props.locales[0]?.code ?? '')
const activeLocale = ref(props.locales[0]?.code ?? '')
// Dots only carry information when there is more than one locale AND translatable fields exist.
const showDots = computed(() => fields.value.translatable.length > 0 && props.locales.length > 1)
function localeFilled(code: string): boolean {
  return hasLocaleContent(fields.value.translatable, model.value.translations[code] ?? {})
}
// The dot is the only signal of per-locale completeness; give it an accessible label
// instead of aria-hiding it outright so screen reader users get the same information.
function dotLabel(code: string): string {
  return t(localeFilled(code) ? 'itemForm.localeComplete' : 'itemForm.localeIncomplete')
}

// Keyed on useId() rather than the bare field name: a schema's field names repeat across every
// item form for that collection, and nothing stops two ItemForm instances (e.g. a list's inline
// edit dialog opened while another item's form is still mounted) from coexisting in the same
// document. A name-only id would make both instances' <label for> resolve to whichever instance's
// control happens to come first in the DOM. The translatable variant folds in the locale code too:
// this field renders once per locale (guarded by the active-tab v-if below), and a label pointing
// at an id that omits the locale would keep resolving to whichever locale rendered it first.
const uid = useId()
function sharedFieldId(name: string): string { return `${uid}-shared-${name}` }
function translatableFieldId(name: string, locale: string): string { return `${uid}-translatable-${name}-${locale}` }
function relationFieldId(name: string): string { return `${uid}-relation-${name}` }
// One error element per error key (`name`, or `locale:name` for a non-default locale), the target of
// the control's aria-describedby.
function errorId(key: string): string { return `${uid}-error-${key}` }
function invalidAttr(key: string): 'true' | undefined { return props.errors[key] ? 'true' : undefined }
function describedByAttr(key: string): string | undefined { return props.errors[key] ? errorId(key) : undefined }

function tabHasErrors(code: string): boolean {
  return localeHasErrors(props.errors, code, fields.value.translatable, defaultCode.value)
}
function fieldError(code: string, name: string): string | undefined {
  return props.errors[translationErrorKey(code, name, defaultCode.value)]
}

const formEl = ref<HTMLFormElement | null>(null)
const bannerEl = ref<HTMLElement | null>(null)

const FOCUSABLE = 'input:not([disabled]):not([type="hidden"]), textarea:not([disabled]), select:not([disabled]), '
  + 'button:not([disabled]), [contenteditable="true"], [tabindex]:not([tabindex="-1"])'

// The key of the first field holding an error, in reading order: the active locale tab's
// translatable fields, then shared fields, then relations.
function firstInvalidKey(): string | undefined {
  const keys = [
    ...fields.value.translatable.map((f) => translationErrorKey(activeLocale.value, f.name, defaultCode.value)),
    ...fields.value.shared.map((f) => f.name),
    ...(props.meta.relations ?? []).map((r) => r.name),
  ]
  return keys.find((key) => Object.hasOwn(props.errors, key))
}

// The control the field's label points at when that is focusable, else the first focusable element
// inside the field (composite fields such as tags or key-value, whose labelled node is a wrapper).
function focusTarget(wrapper: Element): HTMLElement | null {
  const labelFor = wrapper.querySelector('label[for]')?.getAttribute('for')
  const labelled = labelFor ? document.getElementById(labelFor) : null
  if (labelled && wrapper.contains(labelled) && labelled.matches(FOCUSABLE)) return labelled
  return wrapper.querySelector<HTMLElement>(FOCUSABLE)
}

function findFieldWrapper(key: string): Element | undefined {
  const wrappers = formEl.value?.querySelectorAll('[data-field-key]') ?? []
  return [...wrappers].find((el) => el.getAttribute('data-field-key') === key)
}

// A tab switch mounts the new panel on a later tick than the one that changed activeLocale, so the
// wrapper is looked up again for a few ticks before giving up.
const FOCUS_LOOKUP_TICKS = 3
async function focusField(key: string): Promise<void> {
  for (let tick = 0; tick < FOCUS_LOOKUP_TICKS; tick++) {
    const wrapper = findFieldWrapper(key)
    if (wrapper) {
      focusTarget(wrapper)?.focus()
      return
    }
    await nextTick()
  }
}

// Surface validation errors even if the user is on another tab: jump to the first locale tab that
// has an error, the default locale first, then focus the first invalid control. Errors that live
// outside the tabs fall back to the default tab.
watch(() => props.errors, async (e) => {
  if (Object.keys(e).length === 0) return
  const codes = [defaultCode.value, ...props.locales.map((l) => l.code).filter((c) => c !== defaultCode.value)]
  activeLocale.value = codes.find(tabHasErrors) ?? defaultCode.value
  await nextTick()
  const key = firstInvalidKey()
  if (key !== undefined) await focusField(key)
})

// A server error with no field errors has no control to focus, so the banner takes focus.
watch(() => props.serverError, async (message) => {
  if (!message || Object.keys(props.errors).length > 0) return
  await nextTick()
  bannerEl.value?.focus()
})

defineExpose({ activeLocale })
</script>

<template>
  <form ref="formEl" class="item-form grid max-w-[860px] gap-[18px]" @submit.prevent="emit('submit')">
    <p v-if="serverError" ref="bannerEl" class="error text-destructive m-0" role="alert" tabindex="-1">{{ serverError }}</p>

    <Tabs v-if="fields.translatable.length" v-model="activeLocale">
      <TabsList>
        <TabsTrigger v-for="loc in locales" :key="loc.code" :value="loc.code">
          <span
            v-if="showDots"
            class="dot mr-1.5 inline-block h-2 w-2 rounded-full align-middle"
            :class="localeFilled(loc.code) ? 'bg-success' : 'off border-[1.5px] border-border bg-transparent'"
            role="img"
            :aria-label="dotLabel(loc.code)"
          />
          {{ loc.name }}<span v-if="loc.isDefault"> *</span>
          <template v-if="tabHasErrors(loc.code)">
            <span class="tab-error text-destructive ml-1.5 font-bold" aria-hidden="true">!</span>
            <span class="tab-error-text sr-only">{{ t('itemForm.tabHasErrors') }}</span>
          </template>
        </TabsTrigger>
      </TabsList>
      <TabsContent v-for="loc in locales" :key="loc.code" :value="loc.code" class="grid gap-[18px]">
        <template v-if="loc.code === activeLocale">
          <Field
            v-for="f in fields.translatable"
            :key="f.name"
            class="field"
            :data-field-key="translationErrorKey(loc.code, f.name, defaultCode)"
          >
            <div class="lbl-row flex items-center gap-2">
              <FieldLabel :for="translatableFieldId(f.name, loc.code)">{{ f.label }}<span v-if="f.required && (loc.isDefault || localeFilled(loc.code))" class="text-destructive ml-0.5">*</span></FieldLabel>
              <Badge variant="secondary" class="tr-badge" :title="t('itemForm.translatableBadgeHint')">{{ t('itemForm.translatableBadge') }}</Badge>
            </div>
            <FieldInput
              :id="translatableFieldId(f.name, loc.code)"
              :field="f"
              v-model="model.translations[loc.code][f.name]"
              :disabled="disabled"
              :aria-invalid="invalidAttr(translationErrorKey(loc.code, f.name, defaultCode))"
              :aria-describedby="describedByAttr(translationErrorKey(loc.code, f.name, defaultCode))"
            />
            <FieldError
              v-if="fieldError(loc.code, f.name)"
              :id="errorId(translationErrorKey(loc.code, f.name, defaultCode))"
              role="alert"
            >{{ fieldError(loc.code, f.name) }}</FieldError>
          </Field>
        </template>
      </TabsContent>
    </Tabs>

    <Field v-for="f in fields.shared" :key="f.name" class="field" :data-field-key="f.name">
      <FieldLabel :for="sharedFieldId(f.name)">{{ f.label }}<span v-if="f.required" class="text-destructive ml-0.5">*</span></FieldLabel>
      <FieldInput
        :id="sharedFieldId(f.name)"
        :field="f"
        v-model="model.shared[f.name]"
        :disabled="disabled"
        :aria-invalid="invalidAttr(f.name)"
        :aria-describedby="describedByAttr(f.name)"
      />
      <FieldDescription v-if="f.helpText">{{ f.helpText }}</FieldDescription>
      <FieldError v-if="errors[f.name]" :id="errorId(f.name)" role="alert">{{ errors[f.name] }}</FieldError>
    </Field>

    <section v-if="meta.relations && meta.relations.length" class="relations grid gap-3.5">
      <Field v-for="rel in meta.relations" :key="rel.name" class="field" :data-field-key="rel.name">
        <FieldLabel :for="relationFieldId(rel.name)">{{ rel.label }}</FieldLabel>
        <RelationInput
          :id="relationFieldId(rel.name)"
          :relation="rel"
          v-model="model.relations[rel.name]"
          :disabled="disabled"
          :parent-id="itemId"
          :exclude-id="rel.selfReferencing ? itemId : undefined"
          :option-hint="optionHintFor(rel.name)"
          :aria-invalid="invalidAttr(rel.name)"
          :aria-describedby="describedByAttr(rel.name)"
        />
        <FieldDescription v-if="rel.helpText">{{ rel.helpText }}</FieldDescription>
        <FieldError v-if="errors[rel.name]" :id="errorId(rel.name)" role="alert">{{ errors[rel.name] }}</FieldError>
      </Field>
    </section>
  </form>
</template>
