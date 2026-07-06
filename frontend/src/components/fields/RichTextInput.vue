<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import { TableKit } from '@tiptap/extension-table'
import TextAlign from '@tiptap/extension-text-align'
import { TextStyle, Color } from '@tiptap/extension-text-style'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import MediaGrid from '../media/MediaGrid.vue'
import RichTextColorMenu from './RichTextColorMenu.vue'
import RichTextTableMenu from './RichTextTableMenu.vue'
import type { TableAction } from './richTextTableActions'
import type { FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc } from '../../lib/richTextImages'

defineOptions({ name: 'RichTextInput' })

const props = defineProps<{ modelValue: string; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const langStore = useLanguageStore()
const imageDialogOpen = ref(false)
const files = ref<FileRow[]>([])
const imageSearch = ref('')
const imageError = ref('')

async function loadImages(): Promise<void> {
  imageError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: imageSearch.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    files.value = res.data as unknown as FileRow[]
  } catch (e) {
    imageError.value = e instanceof Error ? e.message : 'Failed to load files.'
  }
}

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

onBeforeUnmount(() => editor.value?.destroy())

type Level = 2 | 3

function setLink(): void {
  if (!editor.value) return
  const prev = editor.value.getAttributes('link').href as string | undefined
  const url = window.prompt('Link URL', prev ?? 'https://')
  if (url === null) return
  if (url === '') { editor.value.chain().focus().unsetLink().run(); return }
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
  <div class="rich-text">
    <div v-if="editor" class="rich-text__toolbar">
      <button type="button" data-cmd="bold" :class="{ active: editor.isActive('bold') }"
        :disabled="disabled" aria-label="Bold" title="Bold" @click="editor!.chain().focus().toggleBold().run()"><b>B</b></button>
      <button type="button" data-cmd="italic" :class="{ active: editor.isActive('italic') }"
        :disabled="disabled" aria-label="Italic" title="Italic" @click="editor!.chain().focus().toggleItalic().run()"><i>I</i></button>
      <button type="button" data-cmd="strike" :class="{ active: editor.isActive('strike') }"
        :disabled="disabled" aria-label="Strikethrough" title="Strikethrough" @click="editor!.chain().focus().toggleStrike().run()"><s>S</s></button>
      <button v-for="al in (['left', 'center', 'right', 'justify'] as const)" :key="al" type="button"
        :data-cmd="`align${al.charAt(0).toUpperCase()}${al.slice(1)}`"
        :class="{ active: editor.isActive({ textAlign: al }) }" :disabled="disabled"
        :aria-label="`Align ${al}`" :title="`Align ${al}`"
        @click="editor!.chain().focus().setTextAlign(al).run()"><i :class="`pi pi-align-${al}`" /></button>
      <button v-for="lvl in ([2, 3] as Level[])" :key="lvl" type="button" :data-cmd="`h${lvl}`"
        :class="{ active: editor.isActive('heading', { level: lvl }) }" :disabled="disabled"
        :aria-label="`Heading ${lvl}`" :title="`Heading ${lvl}`"
        @click="editor!.chain().focus().toggleHeading({ level: lvl }).run()">H{{ lvl }}</button>
      <button type="button" data-cmd="subscript" :class="{ active: editor.isActive('subscript') }"
        :disabled="disabled" aria-label="Subscript" title="Subscript"
        @click="editor!.chain().focus().toggleSubscript().run()">x₂</button>
      <button type="button" data-cmd="superscript" :class="{ active: editor.isActive('superscript') }"
        :disabled="disabled" aria-label="Superscript" title="Superscript"
        @click="editor!.chain().focus().toggleSuperscript().run()">x²</button>
      <button type="button" data-cmd="bulletList" :class="{ active: editor.isActive('bulletList') }"
        :disabled="disabled" aria-label="Bullet list" title="Bullet list" @click="editor!.chain().focus().toggleBulletList().run()">• List</button>
      <button type="button" data-cmd="orderedList" :class="{ active: editor.isActive('orderedList') }"
        :disabled="disabled" aria-label="Numbered list" title="Numbered list" @click="editor!.chain().focus().toggleOrderedList().run()">1. List</button>
      <button type="button" data-cmd="blockquote" :class="{ active: editor.isActive('blockquote') }"
        :disabled="disabled" aria-label="Blockquote" title="Blockquote" @click="editor!.chain().focus().toggleBlockquote().run()">&#10077;</button>
      <button type="button" data-cmd="codeBlock" :class="{ active: editor.isActive('codeBlock') }"
        :disabled="disabled" aria-label="Code block" title="Code block" @click="editor!.chain().focus().toggleCodeBlock().run()">{ }</button>
      <button type="button" data-cmd="link" :class="{ active: editor.isActive('link') }"
        :disabled="disabled" aria-label="Link" title="Link" @click="setLink">&#128279;</button>
      <button type="button" data-cmd="hr" :disabled="disabled"
        aria-label="Horizontal rule" title="Horizontal rule" @click="editor!.chain().focus().setHorizontalRule().run()">&#8213;</button>
      <button type="button" data-cmd="image" :disabled="disabled"
        aria-label="Insert image" title="Insert image" @click="openImageDialog">🖼️</button>
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
      <RichTextTableMenu :disabled="disabled" :in-table="editor.isActive('table')" @action="onTableAction" />
      <button type="button" data-cmd="undo" :disabled="disabled"
        aria-label="Undo" title="Undo" @click="editor!.chain().focus().undo().run()">&#8630;</button>
      <button type="button" data-cmd="redo" :disabled="disabled"
        aria-label="Redo" title="Redo" @click="editor!.chain().focus().redo().run()">&#8631;</button>
    </div>
    <EditorContent class="rich-text__content" :editor="editor" />
    <Dialog v-model:visible="imageDialogOpen" modal header="Insert image" :style="{ width: '60rem' }">
      <p v-if="imageError" class="error" role="alert">{{ imageError }}</p>
      <InputText v-model="imageSearch" placeholder="Search files…" class="rich-text__search" @update:model-value="loadImages" />
      <MediaGrid :files="files" selectable @select="onImageSelected" />
    </Dialog>
  </div>
</template>

<style scoped>
.rich-text { border: 1px solid var(--surface-border, #d0d0d0); border-radius: 6px; }
.rich-text__toolbar { display: flex; flex-wrap: wrap; gap: 4px; padding: 6px; border-bottom: 1px solid var(--surface-border, #d0d0d0); }
.rich-text__toolbar button { min-width: 30px; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid transparent; border-radius: 4px; }
.rich-text__toolbar button.active { background: var(--primary-color, #6366f1); color: #fff; }
.rich-text__toolbar button:disabled { opacity: 0.5; cursor: not-allowed; }
.rich-text__content { padding: 10px; min-height: 8rem; }
.rich-text__content :deep(.ProseMirror) { outline: none; min-height: 6rem; }
.rich-text__content :deep(table) { border-collapse: collapse; width: 100%; margin: 8px 0; }
.rich-text__content :deep(th), .rich-text__content :deep(td) { border: 1px solid var(--surface-border, #d0d0d0); padding: 4px 8px; }
.rich-text__content :deep(th) { background: var(--surface-100, #f4f4f5); text-align: left; }
.rich-text__search { display: block; margin: 8px 0 12px; width: 100%; }
</style>
