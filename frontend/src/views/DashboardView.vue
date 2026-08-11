<script setup lang="ts">
// Deliberately empty, not unfinished — otherwise a blank page here is indistinguishable from broken.
import { useI18n } from 'vue-i18n'
import PageHeader from '../components/common/PageHeader.vue'

const { t } = useI18n()
</script>

<template>
  <section>
    <PageHeader :title="t('dashboard.title')" :caption="t('dashboard.comingSoon')" />
  </section>
</template>
