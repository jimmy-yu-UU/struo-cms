<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
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
const { isMobile, setOpenMobile, state, setOpen } = useSidebar()

// Expanded state per real group (default open). Keyed by group name. Explicit v-model
// (rather than Collapsible's own `default-open`) so a collapsed-rail click can force a
// group open — see setGroupOpen below.
const openGroups = reactive<Record<string, boolean>>({})
function isGroupOpen(group: string): boolean {
  return openGroups[group] ?? true
}
function setGroupOpen(group: string, next: boolean): void {
  // Icon-collapsed rail: SidebarMenuSub is CSS-hidden regardless of open state
  // (group-data-[collapsible=icon]:hidden), so a group's items are otherwise
  // permanently unreachable there. A header click while collapsed means "let me see
  // this" — expand the sidebar and force the group open; never toggle an already-open
  // group shut in that state.
  //
  // `state` derives from the desktop `open` ref only, independent of `openMobile` — on mobile
  // the sidebar renders as a Sheet with no group/data-collapsible ancestor, so sub-items are
  // never CSS-hidden there regardless of the desktop cookie. Without the isMobile guard, a
  // persisted collapsed desktop cookie makes every group-header tap on mobile force-expand and
  // silently rewrite that desktop cookie instead of toggling the group.
  if (!isMobile.value && state.value === 'collapsed') {
    setOpen(true)
    openGroups[group] = true
    return
  }
  openGroups[group] = next
}

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

// go() only covers navigation that originates from a click inside this sidebar. Browser
// back/forward (and the OS back gesture) change route.path without ever calling go() — reka's
// Dialog has no history listener of its own, so nothing else closes the mobile drawer for that
// case. Watching route.path directly makes "navigation closes the drawer" structural rather
// than incidental on go()'s own router.push call.
watch(() => route.path, () => {
  if (isMobile.value) setOpenMobile(false)
})
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

    <SidebarContent>
      <div v-if="schema.loadError" class="m-2 grid gap-2 rounded-md border border-destructive p-3 text-sm text-destructive" role="alert">
        <span>{{ schema.loadError }}</span>
        <button type="button" class="justify-self-start rounded-sm border border-border px-3 py-1" @click="schema.load()">
          {{ t('common.retry') }}
        </button>
      </div>

      <!-- SidebarContent renders a plain div; aria-label on a role-less element is
           dropped by assistive tech. `contents` keeps this nav out of the flex layout
           (SidebarContent's flex/gap classes) while still exposing a real navigation
           landmark, replacing the `complementary` landmark the old <aside> gave. -->
      <nav v-else class="contents" :aria-label="t('shell.mainNav')">
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

        <!-- Compensates for a specificity bug in the vendored separator, not a design choice:
             ui/separator/Separator.vue's base classes include
             `data-[orientation=horizontal]:w-full`, and ui/sidebar/SidebarSeparator.vue tries to
             override it with a plain `w-auto` — but that plain class has lower CSS specificity
             than the attribute-selector variant, and tailwind-merge's cn() doesn't dedupe classes
             that carry different modifiers, so both classes reach the DOM and w-full wins
             regardless of source order. The separator (a flex child of SidebarContent, which is
             flex-col) then computes width:100% of its container *before* mx-2 margins are added
             on top, so its right edge lands 8px past SidebarContent's right edge — the 8-16px
             horizontal scrollbar on the sidebar. Re-supplying the *same* modifier here
             (`data-[orientation=horizontal]:w-auto`) lets twMerge recognize the conflict and keep
             only this class, restoring the intended flex-stretch auto-width (container width
             minus the mx-2 insets) with no override fight. If a future re-vendor of
             SidebarSeparator.vue or Separator.vue fixes this upstream (e.g. by using an
             attribute-scoped override itself), this line becomes redundant and can be dropped.
             Any OTHER bare `<SidebarSeparator />` added anywhere in the app hits the same bug and
             needs this same `data-[orientation=horizontal]:w-auto` class — it is not fixed at
             the source, only compensated for at this one call site. This compensation is also
             coupled to the exact modifier string upstream uses today: if a re-vendor renames the
             variant (an `orientation-horizontal:` shorthand, `aria-orientation`, a `**:` wrapper,
             or drops the variant for a plain `w-full`), this class silently stops matching both
             the twMerge conflict key and the specificity contest, and the scrollbar returns with
             no build failure — re-check this comment's premise against the new
             Separator.vue/SidebarSeparator.vue after any re-vendor. The "flex child of
             SidebarContent" framing above also depends on the `class="contents"` on the `<nav>`
             below staying in place — that's what makes SidebarGroup/SidebarSeparator/SidebarGroup
             direct flex items of SidebarContent instead of children of a plain block `<nav>`;
             removing `contents` changes the separator's containing block and this class would need
             re-deriving, not just re-applying. TheSidebar.test.ts asserts the separator's emitted
             class list never contains a bare `w-full`/`:w-full` token as a guard against silent
             regressions here. -->
        <SidebarSeparator class="data-[orientation=horizontal]:w-auto" />

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

                <Collapsible v-else :open="isGroupOpen(g.group)" as-child class="group/collapsible"
                             @update:open="(next: boolean) => setGroupOpen(g.group, next)">
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
      </nav>
    </SidebarContent>

    <SidebarFooter>
      <p class="px-2 text-xs text-muted-foreground group-data-[collapsible=icon]:hidden">
        v0.9.0 · {{ t('shell.version') }}
      </p>
    </SidebarFooter>
  </Sidebar>
</template>
