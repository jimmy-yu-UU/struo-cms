<script setup lang="ts">
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import StatCard from '../components/dashboard/StatCard.vue'
import RecentUpdatesTable from '../components/dashboard/RecentUpdatesTable.vue'
import QuickActions from '../components/dashboard/QuickActions.vue'
import { useDashboardData } from '../composables/useDashboardData'
import type { RecentRow } from '../lib/aggregateRecentUpdates'
import type { QuickAction } from '../lib/quickActions'

const { t } = useI18n()
const router = useRouter()
const { stats, recent, quickActions, greetingKey, loading, error, load } = useDashboardData()

onMounted(load)

function onSelect(row: RecentRow): void {
  void router.push({ name: 'collection-item', params: { name: row.collection, id: row.id } })
}
function onRun(action: QuickAction): void {
  if (action.kind === 'uploadMedia') {
    void router.push({ name: 'media' })
  } else {
    void router.push({ name: 'collection-create', params: { name: action.collection } })
  }
}
</script>

<template>
  <section class="dashboard">
    <header class="dashboard__head">
      <h1>{{ t('dashboard.title') }}</h1>
      <p class="dashboard__greeting">{{ t(`dashboard.greeting.${greetingKey}`) }}</p>
    </header>

    <p v-if="error" class="dashboard__error" role="alert">{{ t('dashboard.error') }}</p>
    <p v-if="loading" class="dashboard__loading">{{ t('dashboard.loading') }}</p>

    <template v-if="!loading">
      <div class="dashboard__stats">
        <StatCard :caption="t('dashboard.stats.items')" :value="stats.items" />
        <StatCard :caption="t('dashboard.stats.collections')" :value="stats.collections" />
        <StatCard v-if="stats.media !== null" :caption="t('dashboard.stats.media')" :value="stats.media" />
        <StatCard v-if="stats.users !== null" :caption="t('dashboard.stats.users')" :value="stats.users" />
      </div>

      <div class="dashboard__grid">
        <div class="card dashboard__recent">
          <h2>{{ t('dashboard.recent.title') }}</h2>
          <RecentUpdatesTable :rows="recent" @select="onSelect" />
        </div>
        <div class="card dashboard__quick">
          <h2>{{ t('dashboard.quick.title') }}</h2>
          <QuickActions :actions="quickActions" @run="onRun" />
        </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.dashboard {
  display: grid;
  gap: 20px;
}
.dashboard__head h1 {
  margin: 0;
}
.dashboard__greeting {
  color: var(--muted);
  margin: 4px 0 0;
}
.dashboard__stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 16px;
}
.dashboard__grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 20px;
  align-items: start;
}
.card {
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--surface);
  padding: 20px;
}
.card h2 {
  margin: 0 0 12px;
  font-size: 1.05rem;
}
.dashboard__error {
  color: var(--danger);
}
.dashboard__loading {
  color: var(--muted);
}
@media (max-width: 860px) {
  .dashboard__grid {
    grid-template-columns: 1fr;
  }
}
</style>
