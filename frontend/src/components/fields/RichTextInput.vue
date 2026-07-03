<script setup lang="ts">
import { watch, onBeforeUnmount } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'

defineOptions({ name: 'RichTextInput' })

const props = defineProps<{ modelValue: string; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const editor = useEditor({
  content: props.modelValue || '',
  editable: !props.disabled,
  extensions: [
    StarterKit.configure({ heading: { levels: [2, 3] } }),
    Link.configure({ openOnClick: false, protocols: ['http', 'https', 'mailto'], autolink: false }),
    Image.configure({ inline: false }),
  ],
  onUpdate: ({ editor }) => emit('update:modelValue', editor.getHTML()),
})

// Keep the editor in sync with external model changes without clobbering the cursor.
watch(() => props.modelValue, (val) => {
  const current = editor.value?.getHTML()
  if (editor.value && val !== current) editor.value.commands.setContent(val || '', { emitUpdate: false })
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

defineExpose({ editor })
</script>

<template>
  <div class="rich-text">
    <div v-if="editor" class="rich-text__toolbar">
      <button type="button" data-cmd="bold" :class="{ active: editor.isActive('bold') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleBold().run()"><b>B</b></button>
      <button type="button" data-cmd="italic" :class="{ active: editor.isActive('italic') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleItalic().run()"><i>I</i></button>
      <button type="button" data-cmd="strike" :class="{ active: editor.isActive('strike') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleStrike().run()"><s>S</s></button>
      <button v-for="lvl in ([2, 3] as Level[])" :key="lvl" type="button" :data-cmd="`h${lvl}`"
        :class="{ active: editor.isActive('heading', { level: lvl }) }" :disabled="disabled"
        @click="editor!.chain().focus().toggleHeading({ level: lvl }).run()">H{{ lvl }}</button>
      <button type="button" data-cmd="bulletList" :class="{ active: editor.isActive('bulletList') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleBulletList().run()">• List</button>
      <button type="button" data-cmd="orderedList" :class="{ active: editor.isActive('orderedList') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleOrderedList().run()">1. List</button>
      <button type="button" data-cmd="blockquote" :class="{ active: editor.isActive('blockquote') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleBlockquote().run()">&#10077;</button>
      <button type="button" data-cmd="codeBlock" :class="{ active: editor.isActive('codeBlock') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleCodeBlock().run()">{ }</button>
      <button type="button" data-cmd="link" :class="{ active: editor.isActive('link') }"
        :disabled="disabled" @click="setLink">&#128279;</button>
      <button type="button" data-cmd="hr" :disabled="disabled"
        @click="editor!.chain().focus().setHorizontalRule().run()">&#8213;</button>
      <button type="button" data-cmd="undo" :disabled="disabled"
        @click="editor!.chain().focus().undo().run()">&#8630;</button>
      <button type="button" data-cmd="redo" :disabled="disabled"
        @click="editor!.chain().focus().redo().run()">&#8631;</button>
    </div>
    <EditorContent class="rich-text__content" :editor="editor" />
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
</style>
