import { describe, expect, it } from 'vitest'
import type { RouteRecordRaw } from 'vue-router'

import { routeNames } from './route-names'
import { routes } from './routes'

interface RouteSummary {
  name?: string | symbol
  path: string
  meta?: { label?: string; requiresAuth?: boolean }
}

function flattenRoutes(
  records: readonly RouteRecordRaw[],
  parentPath = '',
): RouteSummary[] {
  return records.flatMap((record) => {
    const path = record.path.startsWith('/')
      ? record.path
      : `${parentPath}/${record.path}`.replace('//', '/')
    const current: RouteSummary = { name: record.name, path, meta: record.meta }
    const children = record.children
      ? flattenRoutes(record.children, path)
      : []
    return [current, ...children]
  })
}

describe('MkAIHub route skeleton', () => {
  const routeSummaries = flattenRoutes(routes)

  it('registers the six first-level module routes', () => {
    const modulePaths = routeSummaries.map((route) => route.path)

    expect(modulePaths).toEqual(
      expect.arrayContaining([
        '/explore',
        '/tasks',
        '/artifacts',
        '/knowledge',
        '/issues',
        '/competitions',
      ]),
    )
  })

  it('keeps login and admin as explicit routes', () => {
    expect(routeSummaries.find((route) => route.name === routeNames.login)?.path).toBe('/login')
    expect(routeSummaries.find((route) => route.name === routeNames.admin)?.path).toBe('/admin')
  })

  it('marks knowledge as a protected placeholder page', () => {
    const knowledge = routeSummaries.find((route) => route.name === routeNames.knowledge)

    expect(knowledge?.meta).toMatchObject({ label: '知识库', requiresAuth: true })
  })

  it('marks admin as a protected system-admin route', () => {
    const admin = routeSummaries.find((route) => route.name === routeNames.admin)

    expect(admin?.meta).toMatchObject({
      label: '管理',
      requiresAuth: true,
      requiresAdmin: true,
    })
  })
})
