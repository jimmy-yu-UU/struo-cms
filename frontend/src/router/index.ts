import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { authGuard } from './guard'
// AppShell stays a static import: every authenticated route (everything but /login) renders
// inside it, so deferring it would not reduce what any real navigation has to load.
import AppShell from '../layouts/AppShell.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { public: true } },
    {
      path: '/',
      component: AppShell,
      children: [
        { path: '', name: 'dashboard', component: () => import('../views/DashboardView.vue') },
        { path: 'media', name: 'media', component: () => import('../views/MediaLibraryView.vue') },
        { path: 'settings', name: 'settings', component: () => import('../views/SettingsView.vue') },
        { path: 'collections/:name', name: 'collection-list', component: () => import('../views/CollectionListView.vue') },
        { path: 'collections/:name/new', name: 'collection-create', component: () => import('../views/ItemFormView.vue') },
        { path: 'collections/:name/:id', name: 'collection-item', component: () => import('../views/ItemFormView.vue') },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  return authGuard({ name: to.name as string, meta: to.meta }, auth.isAuthenticated)
})

export default router
