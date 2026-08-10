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
  <component :is="ButtonComponent" :is-active="props.active" @click="$emit('activate')">
    <component :is="IconComponent" aria-hidden="true" />
    <span>{{ props.label }}</span>
  </component>
</template>
