<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import {
  Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbList,
  BreadcrumbPage, BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { useSchemaStore } from '@/stores/schemaStore'
import { buildBreadcrumb } from '@/lib/buildBreadcrumb'

const route = useRoute()
const router = useRouter()
const schema = useSchemaStore()
const { t } = useI18n()

// lib/buildBreadcrumb.ts is untouched — only the rendering changes.
const crumbs = computed(() =>
  buildBreadcrumb(
    { name: route.name as string, params: route.params as Record<string, string> },
    schema.collections,
    t,
  ),
)

// BreadcrumbLink renders an <a>. Its default navigation would cancel a router.push that is
// suspended on the unsaved-changes leave guard — the user's "Yes" would then target an
// already-aborted navigation. Same hazard the PrimeVue version guarded against.
function go(event: Event, to: NonNullable<ReturnType<typeof buildBreadcrumb>[number]['to']>): void {
  event.preventDefault()
  router.push(to)
}
</script>

<template>
  <Breadcrumb class="mb-3.5">
    <BreadcrumbList>
      <template v-for="(crumb, index) in crumbs" :key="index">
        <BreadcrumbItem>
          <BreadcrumbLink v-if="crumb.to" href="#" @click="go($event, crumb.to)">
            {{ crumb.label }}
          </BreadcrumbLink>
          <BreadcrumbPage v-else>{{ crumb.label }}</BreadcrumbPage>
        </BreadcrumbItem>
        <BreadcrumbSeparator v-if="index < crumbs.length - 1" />
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>
