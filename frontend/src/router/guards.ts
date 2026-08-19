import type { RouteLocationRaw, RouteLocationNormalized } from 'vue-router'

import { routeNames } from './route-names'

export interface SessionGuardState {
  isInitialized: boolean
  isAuthenticated: boolean
  isAdmin: boolean
  initialize: () => Promise<unknown>
}

function isSafeRedirect(value: unknown): value is string {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')
}

export function loginRedirect(to: Pick<RouteLocationNormalized, 'fullPath'>): RouteLocationRaw {
  return {
    name: routeNames.login,
    query: { redirect: to.fullPath },
  }
}

export async function resolveAuthNavigation(
  to: Pick<RouteLocationNormalized, 'name' | 'fullPath' | 'meta'>,
  session: SessionGuardState,
): Promise<true | RouteLocationRaw> {
  if (!session.isInitialized) {
    try {
      await session.initialize()
    } catch {
      // A failed session probe is treated as anonymous. The login page remains
      // reachable and protected pages receive the normal login redirect.
    }
  }

  if (to.name === routeNames.login && session.isAuthenticated) {
    return { name: routeNames.explore }
  }

  if (to.meta.requiresAuth && !session.isAuthenticated) {
    return loginRedirect(to)
  }

  if (to.meta.requiresAdmin && !session.isAdmin) {
    return { name: routeNames.explore }
  }

  return true
}

export function resolveLoginRedirect(value: unknown): string {
  return isSafeRedirect(value) ? value : '/explore'
}
