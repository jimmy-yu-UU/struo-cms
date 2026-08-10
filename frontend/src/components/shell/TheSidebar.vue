<script setup lang="ts">
import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ChevronRight, Folder } from '@lucide/vue'
import {
  Sidebar, SidebarContent, SidebarFooter, SidebarGroup, SidebarGroupContent, SidebarHeader,
  SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarMenuSub, SidebarMenuSubItem,
  SidebarSeparator, useSidebar,
} from '@/components/ui/sidebar'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { storeToRefs } from 'pinia'
import { useAuthStore } from '@/stores/authStore'
import { useSchemaStore } from '@/stores/schemaStore'
import { useAppConfigStore } from '@/stores/appConfigStore'
import { buildNav } from '@/lib/buildNav'
import SidebarNavItem from './SidebarNavItem.vue'
import BrandMark from './BrandMark.vue'

const UNGROUPED = 'General'

const auth = useAuthStore()
const schema = useSchemaStore()
// Brand name is runtime-editable via Site Settings, so read it from the store rather than
// hardcoding — the sidebar header must track a super-admin's branding change live.
const { brandName } = storeToRefs(useAppConfigStore())
const router = useRouter()
const route = useRoute()
const { t } = useI18n()
const { isMobile, setOpenMobile } = useSidebar()

const canReadMedia = computed(
  () => auth.user?.isSuperAdmin === true || auth.user?.permissions?.file?.read === true,
)
const isSuperAdmin = computed(() => auth.user?.isSuperAdmin === true)

const groups = computed(() =>
  buildNav(schema.collections, auth.user?.isSuperAdmin ?? false, auth.user?.permissions ?? {}),
)

function activeCollection(): string | null {
  const r = route.name as string | undefined
  if (r === 'collection-list' || r === 'collection-create' || r === 'collection-item') {
    return (route.params as Record<string, string>).name ?? null
  }
  return null
}

function go(to: { name: string; params?: Record<string, string> }): void {
  router.push(to)
  // On mobile the sidebar is a Sheet overlaying the content; leaving it open after a
  // navigation would hide the page the user just asked for.
  if (isMobile.value) setOpenMobile(false)
}
</script>

<template>
  <Sidebar collapsible="icon">
    <SidebarHeader>
      <SidebarMenu>
        <SidebarMenuItem>
          <SidebarMenuButton size="lg" :aria-label="t('shell.brandHome')" @click="go({ name: 'dashboard' })">
            <BrandMark />
            <span class="truncate font-semibold">{{ brandName }}</span>
          </SidebarMenuButton>
        </SidebarMenuItem>
      </SidebarMenu>
    </SidebarHeader>

    <SidebarContent :aria-label="t('shell.mainNav')">
      <div v-if="schema.loadError" class="m-2 grid gap-2 rounded-md border border-destructive p-3 text-sm text-destructive" role="alert">
        <span>{{ schema.loadError }}</span>
        <button type="button" class="justify-self-start rounded-sm border border-border px-3 py-1" @click="schema.load()">
          {{ t('common.retry') }}
        </button>
      </div>

      <template v-else>
        <SidebarGroup>
          <SidebarGroupContent>
            <SidebarMenu>
              <SidebarMenuItem>
                <SidebarNavItem :label="t('nav.dashboard')" icon="pi pi-th-large"
                                :active="route.name === 'dashboard'" @activate="go({ name: 'dashboard' })" />
              </SidebarMenuItem>
              <SidebarMenuItem v-if="canReadMedia">
                <SidebarNavItem :label="t('nav.media')" icon="pi pi-images"
                                :active="route.name === 'media'" @activate="go({ name: 'media' })" />
              </SidebarMenuItem>
              <SidebarMenuItem v-if="isSuperAdmin">
                <SidebarNavItem :label="t('nav.settings')" icon="pi pi-cog"
                                :active="route.name === 'settings'" @activate="go({ name: 'settings' })" />
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>

        <SidebarSeparator />

        <SidebarGroup>
          <SidebarGroupContent>
            <SidebarMenu>
              <template v-for="g in groups" :key="g.group">
                <template v-if="g.group === UNGROUPED">
                  <SidebarMenuItem v-for="it in g.items" :key="it.name">
                    <SidebarNavItem :label="it.label" :icon="it.icon"
                                    :active="activeCollection() === it.name"
                                    @activate="go({ name: 'collection-list', params: { name: it.name } })" />
                  </SidebarMenuItem>
                </template>

                <Collapsible v-else default-open as-child class="group/collapsible">
                  <SidebarMenuItem>
                    <CollapsibleTrigger as-child>
                      <SidebarMenuButton>
                        <Folder aria-hidden="true" />
                        <span>{{ g.group }}</span>
                        <ChevronRight class="ml-auto transition-transform group-data-[state=open]/collapsible:rotate-90" aria-hidden="true" />
                      </SidebarMenuButton>
                    </CollapsibleTrigger>
                    <CollapsibleContent>
                      <SidebarMenuSub>
                        <SidebarMenuSubItem v-for="it in g.items" :key="it.name">
                          <SidebarNavItem sub :label="it.label" :icon="it.icon"
                                          :active="activeCollection() === it.name"
                                          @activate="go({ name: 'collection-list', params: { name: it.name } })" />
                        </SidebarMenuSubItem>
                      </SidebarMenuSub>
                    </CollapsibleContent>
                  </SidebarMenuItem>
                </Collapsible>
              </template>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </template>
    </SidebarContent>

    <SidebarFooter>
      <p class="px-2 text-xs text-muted-foreground group-data-[collapsible=icon]:hidden">
        v0.9.0 · {{ t('shell.version') }}
      </p>
    </SidebarFooter>
  </Sidebar>
</template>
