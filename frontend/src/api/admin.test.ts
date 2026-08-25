import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { adminApi } from './admin'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('admin API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('targets the comment moderation endpoints', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    await adminApi.hideComment(9)
    await adminApi.restoreComment(9)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/comments/9/hide')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/admin/comments/9/restore')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('POST')
  })

  it('targets the task reopen endpoint', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 6, status: 'IN_PROGRESS' }))

    await adminApi.reopenTask(6)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/tasks/6/reopen')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
  })

  it('manages competition tasks through the admin endpoints', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 12 }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 12 }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const taskInput = {
      title: '必做任务',
      description: '描述',
      deadline_at: null,
      required: true,
      sort_order: 1,
      max_score: '30.00',
      weight: '10.00',
    }
    await adminApi.createCompetitionTask(7, taskInput)
    await adminApi.updateCompetitionTask(7, 12, { sort_order: 2 })
    await adminApi.deleteCompetitionTask(7, 12)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/competitions/7/tasks')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/admin/competitions/7/tasks/12')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('PATCH')
    expect(String(fetchMock.mock.calls[3][0])).toContain('/admin/competitions/7/tasks/12')
    expect(fetchMock.mock.calls[3][1]?.method).toBe('DELETE')
  })

  it('publishes competition results with optional awards', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 7 }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 7 }))

    await adminApi.publishCompetitionResults(7)
    await adminApi.publishCompetitionResults(7, [{ registration_id: 3, award: '一等奖' }])

    const [, noAwards] = fetchMock.mock.calls[1]
    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/competitions/7/publish-results')
    expect(JSON.parse(String(noAwards?.body))).toEqual({ awards: null })
    const [, withAwards] = fetchMock.mock.calls[2]
    expect(JSON.parse(String(withAwards?.body))).toEqual({
      awards: [{ registration_id: 3, award: '一等奖' }],
    })
  })
})
