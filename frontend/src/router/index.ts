import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { authGuard } from './guard'
import AppShell from '../layouts/AppShell.vue'
import LoginView from '../views/LoginView.vue'
import DashboardView from '../views/DashboardView.vue'
import CollectionListView from '../views/CollectionListView.vue'
import ItemFormView from '../views/ItemFormView.vue'
import MediaLibraryView from '../views/MediaLibraryView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { public: true } },
    {
      path: '/',
      component: AppShell,
      children: [
        { path: '', name: 'dashboard', component: DashboardView },
        { path: 'media', name: 'media', component: MediaLibraryView },
        { path: 'collections/:name', name: 'collection-list', component: CollectionListView },
        { path: 'collections/:name/new', name: 'collection-create', component: ItemFormView },
        { path: 'collections/:name/:id', name: 'collection-item', component: ItemFormView },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  return authGuard({ name: to.name as string, meta: to.meta }, auth.isAuthenticated)
})

export default router
