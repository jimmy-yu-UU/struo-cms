<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import FieldInput from './fields/FieldInput.vue'
import RelationInput from './fields/RelationInput.vue'
import { splitFields } from '../lib/splitFields'
import { hasLocaleContent } from '../lib/localeCompleteness'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const props = defineProps<{
  meta: CollectionMeta
  model: FormModel
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  itemId?: string
}>()
const emit = defineEmits<{ (e: 'submit'): void }>()
const { t } = useI18n()

const fields = computed(() => splitFields(props.meta))
const defaultCode = computed(() => props.locales.find((l) => l.isDefault)?.code ?? props.locales[0]?.code ?? '')
const activeLocale = ref(props.locales[0]?.code ?? '')
// Dots only carry information when there is more than one locale AND translatable fields exist.
const showDots = computed(() => fields.value.translatable.length > 0 && props.locales.length > 1)
function localeFilled(code: string): boolean {
  return hasLocaleContent(fields.value.translatable, props.model.translations[code] ?? {})
}
// FE-29: the dot is the only signal of per-locale completeness; give it an accessible label
// instead of aria-hiding it outright so screen reader users get the same information.
function dotLabel(code: string): string {
  return t(localeFilled(code) ? 'itemForm.localeComplete' : 'itemForm.localeIncomplete')
}

// Surface default-locale validation errors even if the user is on another locale's tab.
watch(() => props.errors, (e) => {
  if (Object.keys(e).length > 0) activeLocale.value = defaultCode.value
})

defineExpose({ activeLocale })
</script>

<template>
  <form class="item-form" @submit.prevent="emit('submit')">
    <p v-if="serverError" class="error" role="alert">{{ serverError }}</p>

    <div v-for="f in fields.shared" :key="f.name" class="field">
      <label :for="f.name">{{ f.label }}<span v-if="f.required" class="req">*</span></label>
      <FieldInput :field="f" v-model="model.shared[f.name]" :disabled="disabled" />
      <small v-if="f.helpText" class="help">{{ f.helpText }}</small>
      <small v-if="errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
    </div>

    <section v-if="meta.relations && meta.relations.length" class="relations">
      <h3>{{ t('itemForm.relations') }}</h3>
      <div v-for="rel in meta.relations" :key="rel.name" class="field">
        <label>{{ rel.label }}</label>
        <RelationInput
          :relation="rel"
          v-model="model.relations[rel.name]"
          :disabled="disabled"
          :parent-id="itemId"
          :exclude-id="rel.selfReferencing ? itemId : undefined"
        />
      </div>
    </section>

    <Tabs v-if="fields.translatable.length" v-model:value="activeLocale">
      <TabList>
        <Tab v-for="loc in locales" :key="loc.code" :value="loc.code">
          <span v-if="showDots" class="dot" :class="{ off: !localeFilled(loc.code) }" role="img" :aria-label="dotLabel(loc.code)" />
          {{ loc.name }}<span v-if="loc.isDefault"> *</span>
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="loc in locales" :key="loc.code" :value="loc.code">
          <template v-if="loc.code === activeLocale">
            <div v-for="f in fields.translatable" :key="f.name" class="field">
              <div class="lbl-row">
                <label>{{ f.label }}<span v-if="f.required && loc.isDefault" class="req">*</span></label>
                <span class="tr-badge">{{ t('itemForm.translatableBadge') }}</span>
              </div>
              <FieldInput :field="f" v-model="model.translations[loc.code][f.name]" :disabled="disabled" />
              <small v-if="loc.isDefault && errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
            </div>
          </template>
        </TabPanel>
      </TabPanels>
    </Tabs>
  </form>
</template>

<style scoped>
.item-form { display: grid; gap: 18px; }
.error { color: var(--danger, #dc2626); margin: 0; }
.field { display: grid; gap: 6px; }
.field label { font-size: 0.9rem; font-weight: 500; color: var(--fg); }
.req { color: var(--danger, #dc2626); margin-left: 2px; }
.help { color: var(--muted); font-size: 0.8rem; }
.field-error { color: var(--danger, #dc2626); font-size: 0.8rem; }
.relations { display: grid; gap: 14px; }
.relations h3 { margin: 0; font-size: 1.125rem; color: var(--fg); }
.lbl-row { display: flex; align-items: center; gap: 8px; }
.tr-badge {
  font-size: 0.68rem; font-weight: 700; padding: 1px 7px; border-radius: 5px;
  background: var(--surface-2, color-mix(in srgb, var(--fg) 8%, transparent)); color: var(--muted);
}
.dot {
  display: inline-block; width: 8px; height: 8px; border-radius: 99px;
  background: var(--success, #16a34a); margin-right: 6px; vertical-align: middle;
}
.dot.off { background: transparent; border: 1.5px solid var(--border); }
</style>
