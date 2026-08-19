import { describe, expect, it, vi } from 'vitest'

import { resolveAuthNavigation, resolveLoginRedirect } from './guards'
import { routeNames } from './route-names'

function target(
  name: string,
  fullPath: string,
  meta: { requiresAuth: boolean; requiresAdmin?: boolean },
) {
  return { name, fullPath, meta } as never
}

describe('route access guard', () => {
  it('redirects an anonymous visitor to login with the original path', async () => {
    const session = {
      isInitialized: true,
      isAuthenticated: false,
      isAdmin: false,
      initialize: vi.fn(),
    }

    await expect(
      resolveAuthNavigation(target('tasks', '/tasks?status=open', { requiresAuth: true }), session),
    ).resolves.toEqual({
      name: routeNames.login,
      query: { redirect: '/tasks?status=open' },
    })
  })

  it('sends an authenticated visitor away from login', async () => {
    const session = {
      isInitialized: true,
      isAuthenticated: true,
      isAdmin: false,
      initialize: vi.fn(),
    }

    await expect(
      resolveAuthNavigation(target(routeNames.login, '/login', { requiresAuth: false }), session),
    ).resolves.toEqual({ name: routeNames.explore })
  })

  it('sends an employee away from the admin route without an admin request', async () => {
    const session = {
      isInitialized: true,
      isAuthenticated: true,
      isAdmin: false,
      initialize: vi.fn(),
    }

    await expect(
      resolveAuthNavigation(
        target(routeNames.admin, '/admin', { requiresAuth: true, requiresAdmin: true }),
        session,
      ),
    ).resolves.toEqual({ name: routeNames.explore })
  })

  it('initializes an unknown session once before deciding access', async () => {
    const initialize = vi.fn(async () => {
      session.isInitialized = true
      session.isAuthenticated = true
    })
    const session = {
      isInitialized: false,
      isAuthenticated: false,
      isAdmin: false,
      initialize,
    }

    await expect(
      resolveAuthNavigation(target(routeNames.explore, '/explore', { requiresAuth: true }), session),
    ).resolves.toBe(true)
    await resolveAuthNavigation(target(routeNames.explore, '/explore', { requiresAuth: true }), session)
    expect(initialize).toHaveBeenCalledOnce()
  })

  it('accepts only internal login redirects', () => {
    expect(resolveLoginRedirect('/artifacts/123')).toBe('/artifacts/123')
    expect(resolveLoginRedirect('//outside.example')).toBe('/explore')
    expect(resolveLoginRedirect('https://outside.example')).toBe('/explore')
  })
})
