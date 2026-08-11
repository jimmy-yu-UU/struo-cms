<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import { TableKit } from '@tiptap/extension-table'
import TextAlign from '@tiptap/extension-text-align'
import { TextStyle, Color } from '@tiptap/extension-text-style'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import {
  AlignLeft, AlignCenter, AlignRight, AlignJustify, List, ListOrdered,
  Quote, Code2, Link as LinkIcon, Minus, Image as ImageIcon, Undo2, Redo2,
} from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import MediaGrid from '../media/MediaGrid.vue'
import RichTextColorMenu from './RichTextColorMenu.vue'
import RichTextTableMenu from './RichTextTableMenu.vue'
import type { TableAction } from './richTextTableActions'
import type { FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc } from '../../lib/richTextImages'
import { isAllowedLinkUrl } from '../../lib/linkUrl'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import { toFileRows } from '../../lib/toFileRow'

defineOptions({ name: 'RichTextInput' })

const props = defineProps<{ modelValue: string; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const { t } = useI18n()
const langStore = useLanguageStore()
const imageDialogOpen = ref(false)
const files = ref<FileRow[]>([])
const imageSearch = ref('')
const imageError = ref('')

const imagesLoad = createLatestWins()
async function loadImages(): Promise<void> {
  const token = imagesLoad.next()
  imageError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: imageSearch.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    if (!imagesLoad.isCurrent(token)) return
    files.value = toFileRows(res.data)
  } catch (e) {
    if (!imagesLoad.isCurrent(token)) return
    imageError.value = e instanceof Error ? e.message : t('fields.loadFilesFailed')
  }
}
// Debounce only search-driven reloads; openImageDialog's direct loadImages() stays immediate.
const debouncedLoadImages = debounce(loadImages, 300)

async function openImageDialog(): Promise<void> {
  imageDialogOpen.value = true
  await loadImages()
}

// Re-derive data-file-id from managed src, then relativize before emitting.
function withFileIds(html: string): string {
  if (!html) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img').forEach((img) => {
    if (img.getAttribute('data-file-id')) return
    const m = (img.getAttribute('src') || '').match(/\/files\/([^/]+)\/content/)
    if (m) img.setAttribute('data-file-id', m[1])
  })
  return doc.body.innerHTML
}

function emitNormalized(): void {
  const html = editor.value?.getHTML() ?? ''
  emit('update:modelValue', relativizeImageSrc(withFileIds(html)))
}

function insertImage(id: string, alt = ''): void {
  if (!editor.value) return
  editor.value.chain().focus().setImage({ src: fileContentDisplayUrl(id), alt }).run()
  emitNormalized()
}

function onImageSelected(id: string): void {
  insertImage(id)
  imageDialogOpen.value = false
}

const editor = useEditor({
  content: absolutizeImageSrc(props.modelValue || ''),
  editable: !props.disabled,
  extensions: [
    StarterKit.configure({ heading: { levels: [2, 3] }, underline: false, link: false }),
    Link.configure({ openOnClick: false, protocols: ['http', 'https', 'mailto'], autolink: false }),
    Image.configure({ inline: false }),
    TableKit.configure({ table: { resizable: false } }),
    TextAlign.configure({ types: ['heading', 'paragraph'], alignments: ['left', 'center', 'right', 'justify'] }),
    TextStyle,
    Color,
    Subscript.extend({ excludes: 'superscript' }),
    Superscript.extend({ excludes: 'subscript' }),
  ],
  onUpdate: () => emitNormalized(),
})

// Keep the editor in sync with external model changes without clobbering the cursor.
watch(() => props.modelValue, (val) => {
  const current = relativizeImageSrc(withFileIds(editor.value?.getHTML() ?? ''))
  if (editor.value && (val || '') !== current) {
    editor.value.commands.setContent(absolutizeImageSrc(val || ''), { emitUpdate: false })
  }
})
watch(() => props.disabled, (d) => editor.value?.setEditable(!d))

onBeforeUnmount(() => {
  editor.value?.destroy()
  debouncedLoadImages.cancel()
})

type Level = 2 | 3

const alignKey = {
  left: 'alignLeft', center: 'alignCenter', right: 'alignRight', justify: 'alignJustify',
} as const
const alignIcon = { left: AlignLeft, center: AlignCenter, right: AlignRight, justify: AlignJustify } as const

function setLink(): void {
  if (!editor.value) return
  const prev = editor.value.getAttributes('link').href as string | undefined
  const url = window.prompt(t('fields.richtext.linkPrompt'), prev ?? 'https://')
  if (url === null) return
  if (url === '') { editor.value.chain().focus().unsetLink().run(); return }
  if (!isAllowedLinkUrl(url)) return // defense-in-depth: silently reject javascript:/data:/etc.
  editor.value.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
}

function onTableAction(action: TableAction): void {
  if (!editor.value) return
  const chain = editor.value.chain().focus()
  const commands: Record<TableAction, () => void> = {
    insert: () => chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
    addRowBefore: () => chain.addRowBefore().run(),
    addRowAfter: () => chain.addRowAfter().run(),
    addColumnBefore: () => chain.addColumnBefore().run(),
    addColumnAfter: () => chain.addColumnAfter().run(),
    deleteRow: () => chain.deleteRow().run(),
    deleteColumn: () => chain.deleteColumn().run(),
    toggleHeaderRow: () => chain.toggleHeaderRow().run(),
    deleteTable: () => chain.deleteTable().run(),
  }
  commands[action]()
}

defineExpose({ editor, insertImage })
</script>

<template>
  <div class="rich-text rounded-md border">
    <div v-if="editor" class="rich-text__toolbar flex flex-wrap gap-1 border-b p-1.5">
      <Button type="button" variant="ghost" size="icon" data-cmd="bold" :data-active="editor.isActive('bold')"
        :disabled="disabled" :aria-label="t('fields.richtext.bold')" :title="t('fields.richtext.bold')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleBold().run()"><b>B</b></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="italic" :data-active="editor.isActive('italic')"
        :disabled="disabled" :aria-label="t('fields.richtext.italic')" :title="t('fields.richtext.italic')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleItalic().run()"><i>I</i></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="strike" :data-active="editor.isActive('strike')"
        :disabled="disabled" :aria-label="t('fields.richtext.strikethrough')" :title="t('fields.richtext.strikethrough')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleStrike().run()"><s>S</s></Button>
      <Button v-for="al in (['left', 'center', 'right', 'justify'] as const)" :key="al" type="button" variant="ghost" size="icon"
        :data-cmd="`align${al.charAt(0).toUpperCase()}${al.slice(1)}`"
        :data-active="editor.isActive({ textAlign: al })" :disabled="disabled"
        :aria-label="t('fields.richtext.' + alignKey[al])" :title="t('fields.richtext.' + alignKey[al])"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground"
        @click="editor!.chain().focus().setTextAlign(al).run()"><component :is="alignIcon[al]" /></Button>
      <Button v-for="lvl in ([2, 3] as Level[])" :key="lvl" type="button" variant="ghost" size="icon" :data-cmd="`h${lvl}`"
        :data-active="editor.isActive('heading', { level: lvl })" :disabled="disabled"
        :aria-label="t(lvl === 2 ? 'fields.richtext.heading2' : 'fields.richtext.heading3')"
        :title="t(lvl === 2 ? 'fields.richtext.heading2' : 'fields.richtext.heading3')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground"
        @click="editor!.chain().focus().toggleHeading({ level: lvl }).run()">H{{ lvl }}</Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="subscript" :data-active="editor.isActive('subscript')"
        :disabled="disabled" :aria-label="t('fields.richtext.subscript')" :title="t('fields.richtext.subscript')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleSubscript().run()">x₂</Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="superscript" :data-active="editor.isActive('superscript')"
        :disabled="disabled" :aria-label="t('fields.richtext.superscript')" :title="t('fields.richtext.superscript')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleSuperscript().run()">x²</Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="bulletList" :data-active="editor.isActive('bulletList')"
        :disabled="disabled" :aria-label="t('fields.richtext.bulletList')" :title="t('fields.richtext.bulletList')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleBulletList().run()"><List /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="orderedList" :data-active="editor.isActive('orderedList')"
        :disabled="disabled" :aria-label="t('fields.richtext.numberedList')" :title="t('fields.richtext.numberedList')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleOrderedList().run()"><ListOrdered /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="blockquote" :data-active="editor.isActive('blockquote')"
        :disabled="disabled" :aria-label="t('fields.richtext.blockquote')" :title="t('fields.richtext.blockquote')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleBlockquote().run()"><Quote /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="codeBlock" :data-active="editor.isActive('codeBlock')"
        :disabled="disabled" :aria-label="t('fields.richtext.codeBlock')" :title="t('fields.richtext.codeBlock')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="editor!.chain().focus().toggleCodeBlock().run()"><Code2 /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="link" :data-active="editor.isActive('link')"
        :disabled="disabled" :aria-label="t('fields.richtext.link')" :title="t('fields.richtext.link')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground" @click="setLink"><LinkIcon /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="hr" :disabled="disabled"
        :aria-label="t('fields.richtext.horizontalRule')" :title="t('fields.richtext.horizontalRule')" @click="editor!.chain().focus().setHorizontalRule().run()"><Minus /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="image" :disabled="disabled"
        :aria-label="t('fields.richtext.insertImage')" :title="t('fields.richtext.insertImage')" @click="openImageDialog"><ImageIcon /></Button>
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
      <RichTextTableMenu :disabled="disabled" :in-table="editor.isActive('table')" @action="onTableAction" />
      <Button type="button" variant="ghost" size="icon" data-cmd="undo" :disabled="disabled"
        :aria-label="t('fields.richtext.undo')" :title="t('fields.richtext.undo')" @click="editor!.chain().focus().undo().run()"><Undo2 /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="redo" :disabled="disabled"
        :aria-label="t('fields.richtext.redo')" :title="t('fields.richtext.redo')" @click="editor!.chain().focus().redo().run()"><Redo2 /></Button>
    </div>
    <EditorContent class="rich-text__content min-h-32 p-2.5 [&_th]:bg-muted" :editor="editor" />
    <!--
      DialogScrollContent, not DialogContent: same defect as FilePicker's file dialog — MediaGrid
      can run to several rows, reka's DialogRoot locks body scroll while open, and plain
      DialogContent is fixed-position/viewport-centered with no scroll container of its own, so
      rows above and below the viewport become unreachable. DialogScrollContent's overlay carries
      its own overflow-y-auto and keeps the content box in normal flow instead.

      Its own width class is an unprefixed max-w-lg (no sm: modifier, unlike plain DialogContent),
      so max-w-4xl below is unprefixed too — matching modifiers is what makes tailwind-merge drop
      the vendored default instead of leaving both classes to fight on source order.
    -->
    <Dialog v-model:open="imageDialogOpen">
      <DialogScrollContent class="max-w-4xl">
        <DialogHeader>
          <DialogTitle>{{ t('fields.richtext.insertImageTitle') }}</DialogTitle>
        </DialogHeader>
        <p v-if="imageError" class="text-destructive" role="alert">{{ imageError }}</p>
        <Input v-model="imageSearch" :placeholder="t('fields.searchFiles')" class="my-1" @update:model-value="debouncedLoadImages" />
        <MediaGrid :files="files" selectable @select="onImageSelected" />
      </DialogScrollContent>
    </Dialog>
  </div>
</template>

<style scoped>
/* TipTap's own generated DOM, not a vendored ui/ component — styling it here is legitimate. */
.rich-text__content :deep(.ProseMirror) { outline: none; min-height: 6rem; }
.rich-text__content :deep(table) { border-collapse: collapse; width: 100%; margin: 8px 0; }
.rich-text__content :deep(th), .rich-text__content :deep(td) { border: 1px solid var(--border); padding: 4px 8px; }
.rich-text__content :deep(th) { text-align: left; }
</style>
