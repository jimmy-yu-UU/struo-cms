<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import FieldInput from './fields/FieldInput.vue'
import { splitFields } from '../lib/splitFields'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const props = defineProps<{
  meta: CollectionMeta
  model: FormModel
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  submitting?: boolean
}>()
const emit = defineEmits<{ (e: 'submit'): void; (e: 'cancel'): void }>()

const fields = computed(() => splitFields(props.meta))
const activeLocale = ref(props.locales[0]?.code ?? '')
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

    <Tabs v-if="fields.translatable.length" v-model:value="activeLocale">
      <TabList>
        <Tab v-for="loc in locales" :key="loc.code" :value="loc.code">
          {{ loc.name }}<span v-if="loc.isDefault"> *</span>
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="loc in locales" :key="loc.code" :value="loc.code">
          <div v-for="f in fields.translatable" :key="f.name" class="field">
            <label>{{ f.label }}<span v-if="f.required && loc.isDefault" class="req">*</span></label>
            <FieldInput :field="f" v-model="model.translations[loc.code][f.name]" :disabled="disabled" />
            <small v-if="loc.isDefault && errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <div class="actions">
      <Button type="button" label="Cancel" severity="secondary" @click="emit('cancel')" />
      <Button v-if="!disabled" type="submit" label="Save" :loading="submitting" />
    </div>
  </form>
</template>
