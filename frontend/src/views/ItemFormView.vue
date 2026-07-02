<script setup lang="ts">
import { reactive, ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import ConfirmDialog from 'primevue/confirmdialog'
import Button from 'primevue/button'
import ItemForm from '../components/ItemForm.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { blankItemForm, parseItemToForm } from '../lib/parseItemToForm'
import { buildItemPayload } from '../lib/buildItemPayload'
import { validateItem } from '../lib/validateItem'
import type { FormModel } from '../types/itemForm'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()

const name = computed(() => route.params.name as string)
const id = computed(() => (route.params.id as string | undefined) ?? undefined)
const isCreate = computed(() => id.value === undefined)
const meta = computed(() => schema.get(name.value))
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))

const model = reactive<FormModel>({ shared: {}, translations: {} })
const errors = ref<Record<string, string>>({})
const serverError = ref('')
const loading = ref(true)
const submitting = ref(false)
const notFound = ref(false)

function setModel(next: FormModel): void {
  model.shared = next.shared
  model.translations = next.translations
}

async function init(): Promise<void> {
  loading.value = true
  serverError.value = ''
  notFound.value = false
  errors.value = {}
  await Promise.all([schema.load(), langStore.load()])
  if (!meta.value) { loading.value = false; return }
  if (isCreate.value) {
    setModel(blankItemForm(meta.value, langStore.languages))
  } else {
    try {
      const item = await itemsApi.get(name.value, id.value!)
      setModel(parseItemToForm(meta.value, item, langStore.languages))
    } catch (e) {
      if (e instanceof Error && /not found/i.test(e.message)) notFound.value = true
      else serverError.value = e instanceof Error ? e.message : 'Failed to load item.'
    }
  }
  loading.value = false
}

async function onSubmit(): Promise<void> {
  if (!meta.value) return
  errors.value = validateItem(meta.value, model, langStore.defaultCode)
  if (Object.keys(errors.value).length > 0) return
  submitting.value = true
  serverError.value = ''
  try {
    const payload = buildItemPayload(meta.value, model, langStore.languages, isCreate.value ? 'create' : 'update')
    if (isCreate.value) await itemsApi.create(name.value, payload)
    else await itemsApi.update(name.value, id.value!, payload)
    router.push({ name: 'collection-list', params: { name: name.value } })
  } catch (e) {
    serverError.value = e instanceof Error ? e.message : 'Save failed.'
  } finally {
    submitting.value = false
  }
}

function onDelete(): void {
  confirm.require({
    header: 'Confirm delete',
    message: 'Delete this item? This cannot be undone.',
    accept: async () => {
      try {
        await itemsApi.remove(name.value, id.value!)
        router.push({ name: 'collection-list', params: { name: name.value } })
      } catch (e) {
        serverError.value = e instanceof Error ? e.message : 'Delete failed.'
      }
    },
  })
}

function onCancel(): void {
  router.push({ name: 'collection-list', params: { name: name.value } })
}

onMounted(init)
defineExpose({ init, onSubmit, onDelete, onCancel, model, errors, serverError, notFound, loading })
</script>

<template>
  <section class="item-form-view">
    <ConfirmDialog />
    <p v-if="loading" class="notice">Loading…</p>
    <p v-else-if="!meta" class="notice">Collection not found.</p>
    <p v-else-if="notFound" class="notice">Item not found.</p>
    <p v-else-if="isCreate && !canWrite" class="notice">You don't have permission to create items here.</p>
    <template v-else>
      <header class="form-header">
        <h2>{{ isCreate ? `New ${meta.label}` : `Edit ${meta.label}` }}</h2>
        <Button v-if="!isCreate && canDelete" label="Delete" severity="danger" @click="onDelete" />
      </header>
      <ItemForm
        :meta="meta"
        :model="model"
        :locales="langStore.languages"
        :errors="errors"
        :server-error="serverError"
        :disabled="!canWrite"
        :submitting="submitting"
        @submit="onSubmit"
        @cancel="onCancel"
      />
    </template>
  </section>
</template>
