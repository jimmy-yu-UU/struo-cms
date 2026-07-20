<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Select from 'primevue/select'
import SelectButton from 'primevue/selectbutton'
import Paginator from 'primevue/paginator'
import PageHeader from '../components/common/PageHeader.vue'
import ListToolbar from '../components/common/ListToolbar.vue'
import TableFooter from '../components/common/TableFooter.vue'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaFileList from '../components/media/MediaFileList.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import MediaDetailDialog from '../components/media/MediaDetailDialog.vue'
import type { FileRow } from '../components/media/FileThumbnail.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useLanguageStore } from '../stores/languageStore'
import { debounce } from '../lib/debounce'
import { createLatestWins } from '../lib/latestWins'
import { mediaTypeFilter, mediaSort, type MediaType, type MediaSort } from '../lib/mediaQuery'

const { t } = useI18n()
const auth = useAuthStore()
const langStore = useLanguageStore()

const files = ref<FileRow[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const page = ref(0)
const perPage = ref(24)
const search = ref('')
const type = ref<MediaType>('all')
const sort = ref<MediaSort>('newest')
const view = ref<'grid' | 'list'>('grid')
const selected = ref<FileRow | null>(null)
const uploadOpen = ref(false)

const canWrite = computed(() => auth.canWrite('file'))
const canDelete = computed(() => auth.canDelete('file'))

const typeOptions = computed(() => [
  { label: t('media.typeAll'), value: 'all' as const },
  { label: t('media.typeImage'), value: 'image' as const },
  { label: t('media.typeVideo'), value: 'video' as const },
])
const sortOptions = computed(() => [
  { label: t('media.sortNewest'), value: 'newest' as const },
  { label: t('media.sortName'), value: 'name' as const },
])
const viewOptions = computed(() => [
  { label: t('media.viewGrid'), value: 'grid' as const, icon: 'pi pi-th-large' },
  { label: t('media.viewList'), value: 'list' as const, icon: 'pi pi-bars' },
])

const mediaLoad = createLatestWins()

async function load(): Promise<void> {
  const token = mediaLoad.next()
  loading.value = true
  error.value = ''
  try {
    await langStore.load()
    const res = await itemsApi.list('file', {
      page: page.value,
      rows: perPage.value,
      sort: mediaSort(sort.value),
      search: search.value || undefined,
      filter: mediaTypeFilter(type.value),
      locale: langStore.defaultCode || undefined,
    })
    if (!mediaLoad.isCurrent(token)) return
    files.value = res.data as unknown as FileRow[]
    total.value = res.total
  } catch (e) {
    if (!mediaLoad.isCurrent(token)) return
    error.value = e instanceof Error ? e.message : t('media.loadFailed')
    files.value = []
    total.value = 0
  } finally {
    if (mediaLoad.isCurrent(token)) loading.value = false
  }
}

function reload(): void {
  page.value = 0
  load()
}

const debouncedSearch = debounce(() => { page.value = 0; load() }, 300)
function onSearchInput(value: string): void {
  search.value = value
  debouncedSearch()
}
function onType(value: MediaType): void { type.value = value; reload() }
function onSort(value: MediaSort): void { sort.value = value; reload() }
function onPage(e: { page: number; rows: number }): void {
  page.value = e.page
  perPage.value = e.rows
  load()
}
function openDetail(id: string): void {
  selected.value = files.value.find((f) => f.id === id) ?? null
}
function onDeleted(): void { selected.value = null; load() }

onMounted(load)
onUnmounted(() => debouncedSearch.cancel())

defineExpose({ load, reload, onType, onSort, onPage, onSearchInput, openDetail, onDeleted,
  files, total, loading, error, canWrite, canDelete, selected })
</script>

<template>
  <section class="media-library">
    <PageHeader :title="t('media.title')" :caption="t('media.count', { n: total })">
      <template #actions>
        <Button v-if="canWrite" :label="t('media.upload')" icon="pi pi-upload" @click="uploadOpen = true" />
      </template>
    </PageHeader>

    <ListToolbar :search-value="search" :search-placeholder="t('media.searchPlaceholder')" @search="onSearchInput">
      <template #filters>
        <Select :model-value="type" :options="typeOptions" option-label="label" option-value="value"
                @update:model-value="onType" />
        <Select :model-value="sort" :options="sortOptions" option-label="label" option-value="value"
                @update:model-value="onSort" />
        <SelectButton v-model="view" :options="viewOptions" option-label="label" option-value="value"
                      :allow-empty="false">
          <template #option="{ option }"><i :class="option.icon" :aria-label="option.label" /></template>
        </SelectButton>
      </template>
    </ListToolbar>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <MediaGrid v-if="view === 'grid'" :files="files" @open="openDetail" />
    <MediaFileList v-else :files="files" @open="openDetail" />
    <p v-if="!loading && !files.length" class="empty">{{ t('media.empty') }}</p>

    <div v-if="total > perPage" class="media-foot">
      <TableFooter :first="page * perPage" :rows="perPage" :total="total" />
      <Paginator :rows="perPage" :total-records="total" :first="page * perPage" @page="onPage" />
    </div>

    <MediaUploadDialog v-model:visible="uploadOpen" @done="reload" />
    <MediaDetailDialog :file="selected" :can-write="canWrite" :can-delete="canDelete"
                       @close="selected = null" @saved="load" @deleted="onDeleted" />
  </section>
</template>

<style scoped>
.media-library { display: block; }
.error { color: var(--danger); font-size: 0.9rem; margin: 0 0 12px; }
.empty { color: var(--muted); text-align: center; padding: 40px 0; }
.media-foot {
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
  margin-top: 16px; flex-wrap: wrap;
}
</style>
