<script setup lang="ts">
// Deliberately empty, not unfinished: the stats/recent-updates/quick-actions dashboard was removed
// (maintainer's call — see the commit that deleted components/dashboard/, composables/useDashboardData.ts,
// and the dashboard-only lib/ helpers). It returns once template 1.0 is stable, then surfacing
// analytics-type data instead of the old self-reported counts.
import { useI18n } from 'vue-i18n'
import PageHeader from '../components/common/PageHeader.vue'

const { t } = useI18n()
</script>

<template>
  <section>
    <PageHeader :title="t('dashboard.title')" :caption="t('dashboard.comingSoon')" />
  </section>
</template>
