import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { authGuard } from './guard'
import AppShell from '../layouts/AppShell.vue'
import LoginView from '../views/LoginView.vue'
import DashboardView from '../views/DashboardView.vue'
import CollectionListView from '../views/CollectionListView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { public: true } },
    {
      path: '/',
      component: AppShell,
      children: [
        { path: '', name: 'dashboard', component: DashboardView },
        { path: 'collections/:name', name: 'collection-list', component: CollectionListView },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  return authGuard({ name: to.name as string, meta: to.meta }, auth.isAuthenticated)
})

export default router
