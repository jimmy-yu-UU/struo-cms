<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Breadcrumb from 'primevue/breadcrumb'
import { useSchemaStore } from '../../stores/schemaStore'
import { buildBreadcrumb } from '../../lib/buildBreadcrumb'

const route = useRoute()
const router = useRouter()
const schema = useSchemaStore()
const { t } = useI18n()

const model = computed(() =>
  buildBreadcrumb(
    { name: route.name as string, params: route.params as Record<string, string> },
    schema.collections,
    t,
  ).map((crumb) => ({
    label: crumb.label,
    command: crumb.to ? () => router.push(crumb.to!) : undefined,
  })),
)
</script>

<template>
  <Breadcrumb :model="model" class="app-breadcrumb" />
</template>
