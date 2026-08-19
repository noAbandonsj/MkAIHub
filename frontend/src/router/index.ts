import { createMemoryHistory, createRouter, createWebHistory, type RouterHistory } from 'vue-router'
import { useSessionStore } from '@/stores/session'

import { resolveAuthNavigation } from './guards'
import { routes } from './routes'

export function createAppRouter(
  history?: RouterHistory,
  getSession: () => ReturnType<typeof useSessionStore> = useSessionStore,
) {
  const router = createRouter({
    history: history ?? createWebHistory(import.meta.env.BASE_URL),
    routes,
    scrollBehavior: () => ({ top: 0 }),
  })

  router.beforeEach((to) => resolveAuthNavigation(to, getSession()))
  return router
}

export { createMemoryHistory }
export * from './route-names'
export * from './routes'
export * from './guards'

const router = createAppRouter()

export default router
