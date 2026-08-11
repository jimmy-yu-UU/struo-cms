<script setup lang="ts">
// Deliberately empty, not unfinished: this is a placeholder for a pending future revisit that
// surfaces analytics-type data instead. The maintainer's call removed the old stats/recent-updates/
// quick-actions dashboard outright, deleting components/dashboard/, composables/useDashboardData.ts,
// and the dashboard-only lib/ helpers along with it.
import { useI18n } from 'vue-i18n'
import PageHeader from '../components/common/PageHeader.vue'

const { t } = useI18n()
</script>

<template>
  <section>
    <PageHeader :title="t('dashboard.title')" :caption="t('dashboard.comingSoon')" />
  </section>
</template>
