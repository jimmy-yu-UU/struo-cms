<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { X } from '@lucide/vue'
import SortableList from '@/components/form/SortableList.vue'
import MediaGrid from '../media/MediaGrid.vue'
import FileThumbnail, { type FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import { toFileRows } from '../../lib/toFileRow'
import type { FieldMeta } from '../../types/schema'

defineOptions({ name: 'FilesField' })

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()

const { t } = useI18n()
const langStore = useLanguageStore()

type Row = FileRow & { missing?: boolean }

function toIds(v: unknown): string[] {
  return Array.isArray(v) ? v.map(String) : []
}

const rows = ref<Row[]>([])
const dialogOpen = ref(false)
const options = ref<FileRow[]>([])
const search = ref('')
const loadError = ref('')

function currentIds(): string[] {
  return rows.value.map((r) => r.id)
}

async function resolve(ids: string[]): Promise<void> {
  if (ids.length === 0) { rows.value = []; return }
  let found: FileRow[] = []
  try {
    const res = await itemsApi.list('file', {
      page: 0,
      rows: ids.length,
      filter: { id: { op: '_in', value: ids.join(',') } },
      locale: langStore.defaultCode || undefined,
    })
    found = toFileRows(res.data)
  } catch {
    found = []
  }
  const byId = new Map(found.map((f) => [f.id, f]))
  // Preserve model order; a missing id becomes a raw-id fallback row.
  rows.value = ids.map(
    (id) => byId.get(id) ?? ({ id, fileName: id, contentType: '', size: 0, missing: true } as Row),
  )
}

// Re-resolve only when the incoming model differs from our current order, so our own
// emits (reorder/add/remove) don't clobber the working rows.
watch(
  () => props.modelValue,
  (v) => {
    const incoming = toIds(v)
    if (JSON.stringify(incoming) !== JSON.stringify(currentIds())) resolve(incoming)
  },
)
onMounted(() => resolve(toIds(props.modelValue)))

function commit(next: Row[]): void {
  rows.value = next
  emit('update:modelValue', next.map((r) => r.id))
}
function onReorder(next: Row[]): void {
  commit([...next])
}
function removeAt(i: number): void {
  commit(rows.value.filter((_, idx) => idx !== i))
}

async function openDialog(): Promise<void> {
  dialogOpen.value = true
  await loadOptions()
}
const optionsLoad = createLatestWins()
async function loadOptions(): Promise<void> {
  const token = optionsLoad.next()
  loadError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0,
      rows: 50,
      search: search.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    if (!optionsLoad.isCurrent(token)) return
    options.value = toFileRows(res.data)
  } catch (e) {
    if (!optionsLoad.isCurrent(token)) return
    loadError.value = e instanceof Error ? e.message : t('fields.loadFilesFailed')
  }
}
function toggle(id: string): void {
  const existing = rows.value.find((r) => r.id === id)
  if (existing) {
    commit(rows.value.filter((r) => r.id !== id))
    return
  }
  const file = options.value.find((f) => f.id === id)
  if (file) commit([...rows.value, file]) // append -> new files go last
}

// Debounce only search-driven reloads; openDialog's direct loadOptions() stays immediate.
const debouncedLoad = debounce(loadOptions, 300)
watch(search, debouncedLoad)
onBeforeUnmount(() => debouncedLoad.cancel())
defineExpose({ openDialog, toggle, removeAt, onReorder, currentIds, resolve, search })
</script>

<template>
  <div class="files-field flex flex-col items-start gap-3">
    <SortableList
      v-if="rows.length"
      :model-value="rows"
      :item-key="(r: Row) => r.id"
      :disabled="disabled"
      @update:model-value="(v: Row[]) => onReorder(v)"
    >
      <template #item="{ item, index }">
        <div class="files-row flex w-full items-center gap-2">
          <FileThumbnail v-if="!item.missing" :file="item" size="sm" />
          <span class="files-row__name min-w-0 flex-1 truncate">{{ item.missing ? item.id : item.fileName }}</span>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :disabled="disabled"
            :aria-label="t('common.delete')"
            @click="removeAt(index)"
          >
            <X class="size-4" />
          </Button>
        </div>
      </template>
    </SortableList>
    <p v-else class="files-field__empty italic text-muted-foreground">{{ t('fields.noFilesSelected') }}</p>

    <Button type="button" variant="outline" size="sm" :disabled="disabled" @click="openDialog">{{ t('fields.selectFiles') }}</Button>

    <Dialog v-model:open="dialogOpen">
      <!--
        DialogScrollContent, not DialogContent: openDialog requests up to 50 files, and reka's
        DialogRoot locks body scroll while open, so a fixed-position, viewport-centered box (plain
        DialogContent) leaves no scroll container for overflow at all -- rows above and below the
        viewport become permanently unreachable. DialogScrollContent's overlay carries its own
        overflow-y-auto and holds the content box in normal flow instead of fixed-centered, so the
        overlay itself scrolls once the box is taller than the viewport.

        Its own width class is a bare max-w-lg (no sm: modifier, unlike plain DialogContent), so the
        override below re-supplies no modifier either -- tailwind-merge keys on (modifier set, class
        group), and a bare max-w-lg only loses to another bare max-w-* class. max-[960px]:max-w-[95vw]
        widens the dialog on medium viewports rather than keeping the 78vw cap.
      -->
      <DialogScrollContent class="max-w-[min(78vw,1300px)] max-[960px]:max-w-[95vw]">
        <DialogHeader>
          <DialogTitle>{{ t('fields.selectFiles') }}</DialogTitle>
        </DialogHeader>
        <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
        <Input v-model="search" :placeholder="t('fields.searchFiles')" :aria-label="t('fields.searchFiles')" />
        <MediaGrid :files="options" multiple :selected-ids="currentIds()" @toggle="toggle" />
        <DialogFooter>
          <Button type="button" @click="dialogOpen = false">{{ t('fields.done') }}</Button>
        </DialogFooter>
      </DialogScrollContent>
    </Dialog>
  </div>
</template>

