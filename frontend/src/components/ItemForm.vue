<script setup lang="ts">
import { ref, computed, watch, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Badge } from '@/components/ui/badge'
import FieldInput from './fields/FieldInput.vue'
import RelationInput from './fields/RelationInput.vue'
import { splitFields } from '../lib/splitFields'
import { hasLocaleContent } from '../lib/localeCompleteness'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const props = defineProps<{
  meta: CollectionMeta
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  itemId?: string
}>()
// The form edits the parent's draft object in place; declaring it as a model makes that
// two-way contract explicit to consumers.
const model = defineModel<FormModel>('model', { required: true })
const emit = defineEmits<{ (e: 'submit'): void }>()
const { t } = useI18n()

const fields = computed(() => splitFields(props.meta))
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

// Surface default-locale validation errors even if the user is on another locale's tab.
watch(() => props.errors, (e) => {
  if (Object.keys(e).length > 0) activeLocale.value = defaultCode.value
})

defineExpose({ activeLocale })
</script>

<template>
  <form class="item-form grid max-w-[860px] gap-[18px]" @submit.prevent="emit('submit')">
    <p v-if="serverError" class="error text-destructive m-0" role="alert">{{ serverError }}</p>

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
        </TabsTrigger>
      </TabsList>
      <TabsContent v-for="loc in locales" :key="loc.code" :value="loc.code" class="grid gap-[18px]">
        <template v-if="loc.code === activeLocale">
          <Field v-for="f in fields.translatable" :key="f.name" class="field">
            <div class="lbl-row flex items-center gap-2">
              <FieldLabel :for="translatableFieldId(f.name, loc.code)">{{ f.label }}<span v-if="f.required && loc.isDefault" class="text-destructive ml-0.5">*</span></FieldLabel>
              <Badge variant="secondary" class="tr-badge">{{ t('itemForm.translatableBadge') }}</Badge>
            </div>
            <FieldInput :id="translatableFieldId(f.name, loc.code)" :field="f" v-model="model.translations[loc.code][f.name]" :disabled="disabled" />
            <FieldError v-if="loc.isDefault && errors[f.name]" role="alert">{{ errors[f.name] }}</FieldError>
          </Field>
        </template>
      </TabsContent>
    </Tabs>

    <Field v-for="f in fields.shared" :key="f.name" class="field">
      <FieldLabel :for="sharedFieldId(f.name)">{{ f.label }}<span v-if="f.required" class="text-destructive ml-0.5">*</span></FieldLabel>
      <FieldInput :id="sharedFieldId(f.name)" :field="f" v-model="model.shared[f.name]" :disabled="disabled" />
      <FieldDescription v-if="f.helpText">{{ f.helpText }}</FieldDescription>
      <FieldError v-if="errors[f.name]" role="alert">{{ errors[f.name] }}</FieldError>
    </Field>

    <section v-if="meta.relations && meta.relations.length" class="relations grid gap-3.5">
      <Field v-for="rel in meta.relations" :key="rel.name" class="field">
        <FieldLabel :for="relationFieldId(rel.name)">{{ rel.label }}</FieldLabel>
        <RelationInput
          :id="relationFieldId(rel.name)"
          :relation="rel"
          v-model="model.relations[rel.name]"
          :disabled="disabled"
          :parent-id="itemId"
          :exclude-id="rel.selfReferencing ? itemId : undefined"
        />
        <FieldError v-if="errors[rel.name]" role="alert">{{ errors[rel.name] }}</FieldError>
      </Field>
    </section>
  </form>
</template>
