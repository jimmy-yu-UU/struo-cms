<script setup lang="ts">
import { computed } from 'vue'
import { SidebarMenuButton, SidebarMenuSubButton } from '@/components/ui/sidebar'
import { resolveIcon } from '@/lib/icons'

const props = withDefaults(
  defineProps<{ label: string; icon?: string | null; active?: boolean; sub?: boolean }>(),
  { icon: null, active: false, sub: false },
)
defineEmits<{ activate: [] }>()

// resolveIcon accepts both dialects: this app's own "pi pi-*" strings and the semantic
// names the backend's [CmsCollection(Icon = "...")] emits.
const IconComponent = computed(() => resolveIcon(props.icon))
// brand-spec §5 gives second-level items their own geometry (h-7 / px-2 with the 1px guide
// rail) — that is exactly what SidebarMenuSubButton is for. Using the top-level button
// inside a SidebarMenuSub would render sub-items at h-8 and lose the rail.
const ButtonComponent = computed(() => (props.sub ? SidebarMenuSubButton : SidebarMenuButton))
</script>

<template>
  <!-- SidebarMenuSubButton's default `as` is "a" with no href, which reka's Primitive
       renders as a plain, keyboard-unreachable, non-interactive-to-AT anchor. Force a real
       <button> for both variants (SidebarMenuButton already defaults to "button"). -->
  <!-- SidebarMenuSubButton (unlike the top-level SidebarMenuButton) carries no `w-full` of its
       own, so it shrinks to its content width and only the label text is clickable. Passing the
       class through here (our own wrapper) rather than editing the vendored component. -->
  <component :is="ButtonComponent" as="button" class="w-full" :is-active="props.active" @click="$emit('activate')">
    <component :is="IconComponent" aria-hidden="true" />
    <span>{{ props.label }}</span>
  </component>
</template>
