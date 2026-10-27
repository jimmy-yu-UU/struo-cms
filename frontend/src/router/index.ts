import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { authGuard, createRouteGuard } from './guard'
// AppShell stays a static import: every authenticated route (everything but /login) renders
// inside it, so it is loaded eagerly.
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

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  const allowed = authGuard({ name: to.name as string, meta: to.meta, fullPath: to.fullPath }, auth.isAuthenticated)
  if (allowed !== true) return allowed
  if (to.name !== 'collection-create') return true
  // The schema is cached and deduped; ItemFormView loads it too. A failed load leaves the flag
  // unknown, which lets the form open and report its own error.
  const schema = useSchemaStore()
  await schema.load().catch(() => undefined)
  const name = typeof to.params.name === 'string' ? to.params.name : ''
  return createRouteGuard({ name: to.name as string, params: to.params }, schema.get(name)?.creatable)
})

export default router
